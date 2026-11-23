using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBench.Infrastructure.Plugins;
using ProtoBench.PluginSdk;

namespace ProtoBench.Application.Tests;

public sealed class PluginUpdateTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "protobench-update-test-" + Guid.NewGuid().ToString("N"));
    private readonly PluginPackageManager packages;
    private const string FeedUrl = "https://plugins.example.test/latest.json";
    private const string PackageUrl = "https://plugins.example.test/latest.nstplugin";

    public PluginUpdateTests()
    {
        packages = new(Path.Combine(directory, "installed"));
        packages.Import(Package("1.0.0"));
    }

    private PluginManifest Manifest(string version, string id = "example.plugin") =>
        new(1, PluginApi.Version, id, "Example", version, "Example.dll", "Example.Plugin", ["Example.dll"]) { UpdateUrl = FeedUrl };

    private string Package(string version, string id = "example.plugin", string? extraPath = null)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".nstplugin");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var writer = new StreamWriter(zip.CreateEntry("plugin.json").Open())) writer.Write(JsonSerializer.Serialize(Manifest(version, id)));
        using (var writer = new StreamWriter(zip.CreateEntry("Example.dll").Open())) writer.Write("not executed during import");
        if (extraPath is not null) zip.CreateEntry(extraPath);
        return path;
    }

    private static PluginUpdateRelease Release(byte[] bytes, string version = "1.1.0") =>
        new("example.plugin", version, PluginApi.Version, PackageUrl, Convert.ToHexString(SHA256.HashData(bytes)), "Example notes");

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }

    private static HttpClient Client(PluginUpdateRelease release, byte[]? package = null) => new(new Handler(request =>
        new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsoluteUri == FeedUrl
            ? new StringContent(JsonSerializer.Serialize(release)) : new ByteArrayContent(package ?? []) }));

    [Fact]
    public async Task DownloadStagesVerifiedUpdateAndPreservesDisabledStateUntilRestart()
    {
        packages.SetEnabled("example.plugin", false);
        var bytes = File.ReadAllBytes(Package("1.1.0"));
        using var http = Client(Release(bytes), bytes);
        var service = new PluginUpdateService(packages, http);
        var release = await service.CheckAsync(Assert.Single(packages.Installed()));
        Assert.NotNull(release);
        await service.DownloadAsync(release);
        var installed = Assert.Single(packages.Installed());
        Assert.Equal("1.0.0", installed.Manifest!.Version);
        Assert.Equal("1.1.0", installed.PendingVersion);
        Assert.False(installed.Enabled);
        Assert.Null(await service.CheckAsync(installed));
        Assert.Empty(packages.ApplyPendingUpdates());
        installed = Assert.Single(packages.Installed());
        Assert.Equal("1.1.0", installed.Manifest!.Version);
        Assert.False(installed.Enabled);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("0.9.0")]
    public async Task EqualVersionsAndDowngradesAreNotOffered(string version)
    {
        using var http = Client(Release([], version));
        Assert.Null(await new PluginUpdateService(packages, http).CheckAsync(Assert.Single(packages.Installed())));
    }

    [Theory]
    [InlineData("http://example.test/feed")]
    [InlineData("https://user:secret@example.test/feed")]
    [InlineData("file:///feed.json")]
    [InlineData("")]
    public void InvalidFeedUrlsAreRejectedAtImport(string url)
    {
        var manifest = Manifest("1.0.0") with { UpdateUrl = url };
        Assert.Throws<InvalidDataException>(() => PluginManifest.Parse(JsonSerializer.Serialize(manifest)));
    }

    [Fact]
    public async Task PluginsWithoutFeedRemainValidAndMakeNoNetworkRequest()
    {
        var manifest = PluginManifest.Parse(JsonSerializer.Serialize(Manifest("1.0.0") with { UpdateUrl = null }));
        using var http = new HttpClient(new Handler(_ => throw new InvalidOperationException("Network must not be used")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new PluginUpdateService(packages, http)
            .CheckAsync(new InstalledPlugin(directory, manifest, true, null)));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("api")]
    [InlineData("url")]
    [InlineData("hash")]
    public async Task InvalidFeedCannotOfferUpdate(string field)
    {
        var release = Release([]);
        release = field switch
        {
            "id" => release with { Id = "another.plugin" },
            "version" => release with { Version = "newest" },
            "api" => release with { ApiVersion = PluginApi.Version + 1 },
            "url" => release with { PackageUrl = "http://example.test/plugin.zip" },
            _ => release with { Sha256 = "not a checksum" }
        };
        using var http = Client(release);
        await Assert.ThrowsAsync<InvalidDataException>(() => new PluginUpdateService(packages, http).CheckAsync(Assert.Single(packages.Installed())));
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("id")]
    [InlineData("version")]
    [InlineData("traversal")]
    public async Task RejectedDownloadPreservesInstalledAndPendingPackages(string failure)
    {
        packages.Import(Package("1.1.0"));
        var bytes = File.ReadAllBytes(Package(failure == "version" ? "1.3.0" : "1.2.0",
            failure == "id" ? "another.plugin" : "example.plugin", failure == "traversal" ? "../escaped.txt" : null));
        var release = Release(bytes, "1.2.0");
        if (failure == "hash") release = release with { Sha256 = new string('0', 64) };
        using var http = Client(release, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => new PluginUpdateService(packages, http).DownloadAsync(release));
        var installed = Assert.Single(packages.Installed());
        Assert.Equal("1.0.0", installed.Manifest!.Version);
        Assert.Equal("1.1.0", installed.PendingVersion);
        Assert.False(File.Exists(Path.Combine(directory, "escaped.txt")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedFeedIsRejectedWithOrWithoutContentLength(bool knownLength)
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        { Content = knownLength ? new ByteArrayContent(new byte[65537]) : new UnknownLengthContent(new byte[65537]) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PluginUpdateService(packages, http).CheckAsync(Assert.Single(packages.Installed())));
    }

    [Fact]
    public async Task NetworkFailureAndCancellationLeaveInstallationIntact()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.ServiceUnavailable)));
        var service = new PluginUpdateService(packages, http);
        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckAsync(Assert.Single(packages.Installed())));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(Release([]), cancel.Token));
        Assert.Null(Assert.Single(packages.Installed()).PendingVersion);
    }

    [Fact]
    public async Task OversizedPackageIsRejectedBeforeReadingItsBody()
    {
        using var http = new HttpClient(new Handler(_ =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentLength = PluginPackageManager.MaxPackageBytes + 1;
            return new(HttpStatusCode.OK) { Content = content };
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PluginUpdateService(packages, http).DownloadAsync(Release([])));
        Assert.Null(Assert.Single(packages.Installed()).PendingVersion);
    }

    [Fact]
    public async Task RedirectToHttpIsRejected()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK)
        {
            RequestMessage = new(HttpMethod.Get, "http://example.test/feed"),
            Content = new StringContent(JsonSerializer.Serialize(Release([])))
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PluginUpdateService(packages, http).CheckAsync(Assert.Single(packages.Installed())));
    }

    [Fact]
    public async Task PrivateGitLabTokenAuthenticatesFeedAndPackageWithoutChangingSharedHeaders()
    {
        var bytes = File.ReadAllBytes(Package("1.1.0"));
        var release = Release(bytes);
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Equal("test-private-token", Assert.Single(request.Headers.GetValues("PRIVATE-TOKEN")));
            Assert.DoesNotContain("test-private-token", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.OK) { Content = request.RequestUri.AbsoluteUri == FeedUrl
                ? new StringContent(JsonSerializer.Serialize(release)) : new ByteArrayContent(bytes) };
        }));
        var service = new PluginUpdateService(packages, http, (id, source) =>
        {
            Assert.Equal("example.plugin", id);
            Assert.Equal(new Uri(FeedUrl), source);
            return "test-private-token";
        });
        var found = await service.CheckAsync(Assert.Single(packages.Installed()));
        await service.DownloadAsync(Assert.IsType<PluginUpdateRelease>(found));
        Assert.Equal(2, requests);
        Assert.False(http.DefaultRequestHeaders.Contains("PRIVATE-TOKEN"));
        Assert.Equal("1.1.0", Assert.Single(packages.Installed()).PendingVersion);
    }

    [Theory]
    [InlineData("https://cdn.example.test/package.nstplugin")]
    [InlineData("https://plugins.example.test:8443/package.nstplugin")]
    public async Task PackageOnAnotherOriginNeverReceivesGitLabToken(string packageUrl)
    {
        var bytes = File.ReadAllBytes(Package("1.1.0"));
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.False(request.Headers.Contains("PRIVATE-TOKEN"));
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }));
        await new PluginUpdateService(packages, http, (_, _) => "test-private-token")
            .DownloadAsync(Release(bytes) with { PackageUrl = packageUrl });
        Assert.Equal("1.1.0", Assert.Single(packages.Installed()).PendingVersion);
    }

    [Theory]
    [InlineData("https://other.example.test/latest.json", false)]
    [InlineData("https://plugins.example.test:8443/latest.json", false)]
    [InlineData("/redirected.json", true)]
    public async Task RedirectsAuthenticateOnlyTheOriginalOrigin(string destination, bool expectToken)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            calls++;
            if (calls == 1)
            {
                Assert.True(request.Headers.Contains("PRIVATE-TOKEN"));
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new(destination, UriKind.RelativeOrAbsolute);
                return response;
            }
            Assert.Equal(expectToken, request.Headers.Contains("PRIVATE-TOKEN"));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(Release([]))) };
        }));
        Assert.NotNull(await new PluginUpdateService(packages, http, (_, _) => "test-private-token")
            .CheckAsync(Assert.Single(packages.Installed())));
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task HttpRedirectIsBlockedBeforeAnySecondRequest()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new("http://plugins.example.test/latest.json");
            return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PluginUpdateService(packages, http, (_, _) => "test-private-token")
            .CheckAsync(Assert.Single(packages.Installed())));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task PrivateProjectAccessFailuresExplainHowToReplaceTokenWithoutExposingIt(int status)
    {
        using var http = new HttpClient(new Handler(_ => new((HttpStatusCode)status)));
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => new PluginUpdateService(packages, http, (_, _) => "test-private-token")
            .CheckAsync(Assert.Single(packages.Installed())));
        Assert.Contains("Plugin manager", exception.Message);
        Assert.DoesNotContain("test-private-token", exception.Message);
    }

    [Fact]
    public async Task RedirectLoopsAreBounded()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new("/latest.json", UriKind.Relative);
            return response;
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new PluginUpdateService(packages, http).CheckAsync(Assert.Single(packages.Installed())));
        Assert.Equal(6, calls);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
