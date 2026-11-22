using System.Net;
using ProtoBench.Infrastructure.Updates;
using ProtoBench.Desktop.Services;
using ProtoBench.Core.Documents;
using Velopack.Sources;

namespace ProtoBench.Application.Tests;

public sealed class UpdateProxyTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("proxy.company:8080", "http://proxy.company:8080")]
    [InlineData(" http://proxy.company:8080/ ", "http://proxy.company:8080")]
    [InlineData("https://proxy.company:8443", "https://proxy.company:8443")]
    [InlineData("http://127.0.0.1:3128", "http://127.0.0.1:3128")]
    public void NormalizesProxyAddress(string value, string expected) => Assert.Equal(expected, UpdateProxyAddress.Normalize(value));

    [Theory]
    [InlineData("ftp://proxy.company:8080")]
    [InlineData("http://user:password@proxy.company:8080")]
    [InlineData("http://proxy.company:8080/path")]
    [InlineData("http://proxy.company:8080?token=secret")]
    [InlineData("http://proxy.company:8080#fragment")]
    [InlineData("http://proxy.company:99999")]
    [InlineData("http://proxy.company:0")]
    [InlineData("not a valid host")]
    public void RejectsInvalidProxyAddress(string value) => Assert.Throws<ArgumentException>(() => UpdateProxyAddress.Normalize(value));

    [Fact]
    public async Task SuccessfulNormalConnectionDoesNotCreateProxyAndKeepsRequestHeaders()
    {
        var direct = new FakeDownloader();
        var proxyCreates = 0;
        var downloader = new UpdateProxyDownloader(() => "http://proxy:8080", direct, _ => { proxyCreates++; return new FakeDownloader(); });
        var headers = new Dictionary<string, string> { ["Accept"] = "application/json" };
        Assert.Equal("ok", await downloader.DownloadString("https://updates.example/latest.json", headers));
        Assert.Same(headers, direct.Headers);
        Assert.Equal(.5, direct.Timeout); // Velopack specifies timeout in minutes; metadata waits at most 30 seconds per route.
        Assert.Equal(1, direct.Calls);
        Assert.Equal(0, proxyCreates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.ProxyAuthenticationRequired)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task NetworkAndBlockingFailuresRetryOnceThroughSavedProxy(HttpStatusCode? status)
    {
        var direct = new FakeDownloader { Error = new HttpRequestException("Blocked", null, status) };
        var proxy = new FakeDownloader(); Uri? received = null;
        var downloader = new UpdateProxyDownloader(() => "proxy.company:8080", direct, address => { received = address; return proxy; });
        Assert.Equal("ok", await downloader.DownloadString("https://updates.example/latest.json"));
        Assert.Equal(new Uri("http://proxy.company:8080"), received);
        Assert.Equal(1, direct.Calls); Assert.Equal(1, proxy.Calls);
    }

    [Fact]
    public async Task TimeoutRetriesButExplicitCancellationDoesNot()
    {
        var direct = new FakeDownloader { Error = new TaskCanceledException("Timeout") };
        var proxy = new FakeDownloader();
        var downloader = new UpdateProxyDownloader(() => "http://proxy:8080", direct, _ => proxy);
        Assert.Equal("ok", await downloader.DownloadString("https://updates.example/feed"));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadFile("https://updates.example/pkg", "unused", _ => { }, cancelToken: cancelled.Token));
        Assert.Equal(1, proxy.Calls);
    }

    [Fact]
    public async Task EmptyProxySettingDoesNotRetry()
    {
        var direct = new FakeDownloader { Error = new HttpRequestException("Offline") };
        var downloader = new UpdateProxyDownloader(() => "", direct, _ => throw new Exception("Should not create proxy"));
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadString("https://updates.example/feed"));
        Assert.Equal(1, direct.Calls);
    }

    [Fact]
    public async Task MissingAssetAndDiskErrorsAreNotRetried()
    {
        var direct = new FakeDownloader { Error = new HttpRequestException("Not found", null, HttpStatusCode.NotFound) };
        var downloader = new UpdateProxyDownloader(() => "http://proxy:8080", direct, _ => throw new Exception("Should not create proxy"));
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadString("https://updates.example/feed"));
        direct.Error = new IOException("Disk full");
        await Assert.ThrowsAsync<IOException>(() => downloader.DownloadFile("https://updates.example/pkg", "unused", _ => { }));
    }

    [Fact]
    public async Task ProxyFailureIsReturnedWithoutRetryLoop()
    {
        var direct = new FakeDownloader { Error = new HttpRequestException("Direct blocked") };
        var proxy = new FakeDownloader { Error = new HttpRequestException("Proxy unreachable") };
        var downloader = new UpdateProxyDownloader(() => "http://proxy:8080", direct, _ => proxy);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadString("https://updates.example/feed"));
        Assert.Equal("Proxy unreachable", error.Message);
        Assert.Equal(1, direct.Calls); Assert.Equal(1, proxy.Calls);
    }

    [Fact]
    public async Task ChangedSettingIsReadWithoutRecreatingDownloader()
    {
        var address = "http://first:8080";
        var received = new List<string>();
        var downloader = new UpdateProxyDownloader(() => address, new FakeDownloader { Error = new HttpRequestException("Blocked") },
            uri => { received.Add(uri.Host); return new FakeDownloader(); });
        await downloader.DownloadString("https://updates.example/feed");
        address = "http://second:8080";
        Assert.Equal("ok"u8.ToArray(), await downloader.DownloadBytes("https://updates.example/feed"));
        Assert.Equal(new[] { "first", "second" }, received);
    }

    [Fact]
    public async Task PackageFallbackTruncatesPartialFileAndPreservesTimeoutAndProgress()
    {
        var path = Path.Combine(Path.GetTempPath(), "proxy-download-" + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(path, "a long partial download");
            var direct = new FakeDownloader { Error = new HttpRequestException("Connection failed") };
            var proxy = new FakeDownloader();
            var downloader = new UpdateProxyDownloader(() => "http://proxy:8080", direct, _ => proxy);
            var progress = new List<int>();
            await downloader.DownloadFile("https://updates.example/pkg", path, progress.Add, timeout: 10);
            Assert.Equal("ok", await File.ReadAllTextAsync(path));
            Assert.Equal(10, proxy.Timeout);
            Assert.Equal(new[] { 100 }, progress);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PortableManifestCheckUsesProxyDownloader()
    {
        var direct = new FakeDownloader { Error = new HttpRequestException("Blocked") };
        var proxy = new FakeDownloader { Text = "{\"version\":\"2.0.0\",\"releaseNotes\":\"Changes\"}" };
        var downloader = new UpdateProxyDownloader(() => "http://proxy:8080", direct, _ => proxy);
        using var http = new HttpClient();
        var options = new UpdateOptions(true, "https://updates.example/latest.json", "https://updates.example/download", "https://updates.example/feed");
        var service = new UpdateService(options, "1.0.0", http, () => new PortableUpdater(), downloader);
        Assert.Equal("2.0.0", (await service.CheckAsync())!.Version);
        Assert.Equal(1, proxy.Calls);
    }

    [Fact]
    public async Task RealHttpHandlerUsesConfiguredProxyWithoutContactingOrigin()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        async Task<string> Serve()
        {
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);
            var firstLine = await reader.ReadLineAsync(timeout.Token);
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 }) { }
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), timeout.Token);
            return firstLine!;
        }
        var server = Serve();
        var downloader = new UpdateProxyDownloader(() => $"http://127.0.0.1:{endpoint.Port}",
            new FakeDownloader { Error = new HttpRequestException("Blocked") });
        // Plain HTTP here exercises proxy routing on loopback; production update sources still require HTTPS.
        Assert.Equal("ok", await downloader.DownloadString("http://updates.example/latest.json").WaitAsync(timeout.Token));
        Assert.Equal("GET http://updates.example/latest.json HTTP/1.1", await server);
    }

    [Fact]
    public async Task ProxyIsSavedNormalizedAndInvalidEditsDoNotReplaceSavedSetting()
    {
        var store = new MemoryStore(); var settings = new SessionSettings(store);
        await settings.SetUpdateProxyAsync(" proxy.company:8080 ");
        var reloaded = new SessionSettings(store); await reloaded.LoadAsync();
        Assert.Equal("http://proxy.company:8080", reloaded.Current.UpdateProxyAddress);
        await Assert.ThrowsAsync<ArgumentException>(() => settings.SetUpdateProxyAsync("ftp://invalid"));
        Assert.Equal("http://proxy.company:8080", settings.Current.UpdateProxyAddress);
        await settings.SetUpdateProxyAsync(""); Assert.Empty(store.Settings.UpdateProxyAddress);
    }

    private sealed class FakeDownloader : IFileDownloader
    {
        public int Calls { get; private set; }
        public Exception? Error { get; set; }
        public string Text { get; init; } = "ok";
        public IDictionary<string, string>? Headers { get; private set; }
        public double Timeout { get; private set; }
        private void Begin(IDictionary<string, string>? headers, double timeout)
        { Calls++; Headers = headers; Timeout = timeout; if (Error is not null) throw Error; }
        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        { Begin(headers, timeout); return Task.FromResult(Text); }
        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        { Begin(headers, timeout); return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(Text)); }
        public async Task DownloadFile(string url, string targetFile, Action<int> progress,
            IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested(); Begin(headers, timeout);
            await using var stream = File.OpenWrite(targetFile);
            await stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(Text), token); progress(100);
        }
    }

    private sealed class PortableUpdater : IInstalledUpdater
    {
        public bool IsInstalled => false;
        public Task<UpdateRelease?> CheckAsync() => throw new InvalidOperationException();
        public Task DownloadAsync() => throw new InvalidOperationException();
        public void Restart() => throw new InvalidOperationException();
    }

    private sealed class MemoryStore : ISettingsStore
    {
        public LocalSettings Settings { get; private set; } = new();
        public Task<LocalSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);
        public Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default) { Settings = settings; return Task.CompletedTask; }
    }
}
