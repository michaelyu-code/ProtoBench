using System.Net;
using ProtoBench.Infrastructure.Updates;
using Velopack.Logging;
using Velopack.Sources;

namespace ProtoBench.Application.Tests;

public sealed class UpdateTests
{
    private static readonly UpdateOptions Enabled = new(true, "https://updates.example/latest.json", "https://updates.example/download", "https://updates.example/feed");

    [Fact]
    public async Task StartupCheckIsSharedAndNeverDownloadsOrRestarts()
    {
        using var http = new HttpClient(new ManifestHandler("must not be fetched"));
        var completion = new TaskCompletionSource<UpdateRelease?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeUpdater(true) { CheckTask = completion.Task };
        var service = new UpdateService(Enabled, "1.0.0", http, () => backend);
        var first = service.CheckOnStartupAsync();
        var second = service.CheckOnStartupAsync();
        Assert.Same(first, second); Assert.False(first.IsCompleted); Assert.Equal(1, backend.Checks);
        completion.SetResult(backend.Release);
        Assert.Equal("2.0.0", (await first)!.Version);
        Assert.Equal(backend.Release, await service.CheckOnStartupAsync());
        Assert.Equal(1, backend.Checks); Assert.Equal(0, backend.Downloads); Assert.False(backend.Restarted);
        await service.CheckAsync(); Assert.Equal(2, backend.Checks);
    }

    [Fact]
    public async Task StartupFailureCanBeRetriedManually()
    {
        using var http = new HttpClient(new ManifestHandler("must not be fetched"));
        var backend = new FakeUpdater(true) { CheckTask = Task.FromException<UpdateRelease?>(new IOException("Offline")) };
        var service = new UpdateService(Enabled, "1.0.0", http, () => backend);
        await Assert.ThrowsAsync<IOException>(() => service.CheckOnStartupAsync());
        backend.CheckTask = null;
        Assert.NotNull(await service.CheckAsync());
        Assert.Equal(2, backend.Checks); Assert.Equal(0, backend.Downloads);
    }

    [Fact]
    public async Task DisabledStartupCheckDoesNotCreateBackendOrSendRequests()
    {
        var handler = new ManifestHandler("must not be fetched");
        using var http = new HttpClient(handler);
        var service = new UpdateService(new(), "1.0.0", http, () => throw new Exception("Backend must not be created"));
        Assert.Null(await service.CheckOnStartupAsync());
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task GitHubSourceReadsStableReleaseFeedAndDownloadsItsPackage()
    {
        var downloader = new GitHubDownloader();
        var source = VelopackUpdater.CreateSource("https://github.com/example/ProtoBench", downloader);
        Assert.IsType<GithubSource>(source);
        var feed = await source.GetReleaseFeed(new TestLogger(), "ProtoBench.Desktop", "win", null, null);
        var asset = Assert.Single(feed.Assets);
        Assert.Equal("2.0.0", asset.Version.ToString());
        Assert.DoesNotContain(downloader.Requests, url => url.Contains("v3.0.0-beta"));
        var target = Path.Combine(Path.GetTempPath(), "protobench-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            await source.DownloadReleaseEntry(new TestLogger(), asset, target, _ => { }, CancellationToken.None);
            Assert.Equal("package bytes", await File.ReadAllTextAsync(target));
            Assert.Contains("https://github.com/example/ProtoBench/releases/download/v2.0.0/ProtoBench.Desktop-2.0.0-full.nupkg", downloader.Requests);
        }
        finally { File.Delete(target); }
    }

    [Theory]
    [InlineData("http://github.com/example/ProtoBench")]
    [InlineData("https://user:password@github.com/example/ProtoBench")]
    [InlineData("https://github.com/example/ProtoBench/releases")]
    [InlineData("https://github.com/example/ProtoBench?token=secret")]
    public void InvalidSourcesAreRejected(string url) => Assert.Throws<ArgumentException>(() => VelopackUpdater.CreateSource(url));

    [Fact]
    public void GenericHttpsFeedRemainsSupported() =>
        Assert.IsType<SimpleWebSource>(VelopackUpdater.CreateSource("https://updates.example/feed"));

    private sealed class TestLogger : IVelopackLogger
    {
        public void Log(VelopackLogLevel level, string? message, Exception? exception) { }
    }

    private sealed class GitHubDownloader : IFileDownloader
    {
        public List<string> Requests { get; } = [];
        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Requests.Add(url);
            if (url.StartsWith("https://api.github.com/repos/example/ProtoBench/releases", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult("""
                [
                  {"tag_name":"v3.0.0-beta","prerelease":true,"published_at":"2026-11-02T00:00:00Z","assets":[
                    {"name":"releases.win.json","browser_download_url":"https://github.com/example/ProtoBench/releases/download/v3.0.0-beta/releases.win.json"}]},
                  {"tag_name":"v2.0.0","prerelease":false,"published_at":"2026-11-01T00:00:00Z","assets":[
                    {"name":"releases.win.json","browser_download_url":"https://github.com/example/ProtoBench/releases/download/v2.0.0/releases.win.json"},
                    {"name":"ProtoBench.Desktop-2.0.0-full.nupkg","browser_download_url":"https://github.com/example/ProtoBench/releases/download/v2.0.0/ProtoBench.Desktop-2.0.0-full.nupkg"}]}
                ]
                """);
            if (url == "https://github.com/example/ProtoBench/releases/download/v2.0.0/releases.win.json")
                return Task.FromResult("""
                {"Assets":[{"PackageId":"ProtoBench.Desktop","Version":"2.0.0","Type":"Full","FileName":"ProtoBench.Desktop-2.0.0-full.nupkg","SHA1":"0000000000000000000000000000000000000000","Size":13}]}
                """);
            throw new InvalidOperationException("Unexpected update request: " + url);
        }
        public async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            System.Text.Encoding.UTF8.GetBytes(await DownloadString(url, headers, timeout));
        public async Task DownloadFile(string url, string targetFile, Action<int> progress,
            IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken token = default)
        {
            Requests.Add(url);
            await File.WriteAllTextAsync(targetFile, "package bytes", token);
        }
    }

    [Fact]
    public async Task DisabledBlocksEveryActionBeforeCreatingBackendOrSendingRequests()
    {
        var handler = new ManifestHandler("{}");
        using var http = new HttpClient(handler);
        var service = new UpdateService(new(), "1.0.0", http, () => throw new Exception("Backend must not be created"));
        Assert.False(service.Enabled);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync());
        Assert.Throws<InvalidOperationException>(() => service.Restart());
        Assert.Throws<InvalidOperationException>(() => service.DownloadPage);
        Assert.Equal(0, handler.Requests);
    }

    [Theory]
    [InlineData("1.10.0", true)]
    [InlineData("1.2.0", false)]
    [InlineData("1.1.0", false)]
    public async Task FolderCopyComparesSemanticVersionsAndUsesConfiguredDownloadPage(string version, bool expected)
    {
        var handler = new ManifestHandler($$"""{"version":"{{version}}","releaseNotes":"Changes"}""");
        using var http = new HttpClient(handler);
        var service = new UpdateService(Enabled, "1.2.0+build", http, () => new FakeUpdater(false));
        var release = await service.CheckAsync();
        Assert.Equal(expected, release is not null);
        if (release is not null) { Assert.False(release.CanInstall); Assert.Equal("Changes", release.ReleaseNotes); }
        Assert.Equal(new Uri(Enabled.DownloadPageUrl), service.DownloadPage);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync());
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"version\":\"invalid\"}")]
    [InlineData("{\"version\":\"2.0.0-beta\"}")]
    public async Task InvalidManifestCannotOfferUpdate(string json)
    {
        using var http = new HttpClient(new ManifestHandler(json));
        var service = new UpdateService(Enabled, "1.0.0", http, () => new FakeUpdater(false));
        await Assert.ThrowsAnyAsync<Exception>(() => service.CheckAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync());
    }

    [Fact]
    public async Task InstalledUpdateRequiresCheckAndDownloadBeforeExplicitRestart()
    {
        var handler = new ManifestHandler("must not be fetched");
        using var http = new HttpClient(handler);
        var backend = new FakeUpdater(true);
        var service = new UpdateService(Enabled, "1.0.0", http, () => backend);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync());
        Assert.True((await service.CheckAsync())!.CanInstall);
        Assert.Throws<InvalidOperationException>(() => service.Restart());
        backend.FailDownload = true;
        await Assert.ThrowsAsync<IOException>(() => service.DownloadAsync());
        Assert.Throws<InvalidOperationException>(() => service.Restart());
        backend.FailDownload = false;
        await service.DownloadAsync();
        Assert.False(backend.Restarted);
        service.Restart();
        Assert.True(backend.Restarted);
        backend.Release = null;
        Assert.Null(await service.CheckAsync());
        Assert.Throws<InvalidOperationException>(() => service.Restart());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAsync());
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task EnabledButMisconfiguredNeverCreatesBackend()
    {
        using var http = new HttpClient(new ManifestHandler("{}"));
        var service = new UpdateService(Enabled with { FeedUrl = "http://updates.example/feed" }, "1.0.0", http,
            () => throw new Exception("Backend must not be created"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync());
    }

    private sealed class ManifestHandler(string json) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private sealed class FakeUpdater(bool installed) : IInstalledUpdater
    {
        public bool IsInstalled => installed;
        public UpdateRelease? Release { get; set; } = new("2.0.0", "Changes", true);
        public bool Restarted { get; private set; }
        public bool FailDownload { get; set; }
        public Task<UpdateRelease?>? CheckTask { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public Task<UpdateRelease?> CheckAsync() { Checks++; return CheckTask ?? Task.FromResult(Release); }
        public Task DownloadAsync() { Downloads++; return FailDownload ? Task.FromException(new IOException("Failed")) : Task.CompletedTask; }
        public void Restart() => Restarted = true;
    }
}
