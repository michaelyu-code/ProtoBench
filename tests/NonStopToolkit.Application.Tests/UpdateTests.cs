using System.Net;
using NonStopToolkit.Infrastructure.Updates;

namespace NonStopToolkit.Application.Tests;

public sealed class UpdateTests
{
    private static readonly UpdateOptions Enabled = new(true, "https://updates.example/latest.json", "https://updates.example/download", "https://updates.example/feed");

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
        public Task<UpdateRelease?> CheckAsync() => Task.FromResult(Release);
        public Task DownloadAsync() => FailDownload ? Task.FromException(new IOException("Failed")) : Task.CompletedTask;
        public void Restart() => Restarted = true;
    }
}
