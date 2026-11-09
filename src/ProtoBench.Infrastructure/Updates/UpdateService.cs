using System.Net.Http;
using System.Text.Json;
using Velopack;

namespace ProtoBench.Infrastructure.Updates;

public sealed record UpdateRelease(string Version, string ReleaseNotes, bool CanInstall);

public interface IInstalledUpdater
{
    bool IsInstalled { get; }
    Task<UpdateRelease?> CheckAsync();
    Task DownloadAsync();
    void Restart();
}

public sealed class UpdateService(UpdateOptions options, string currentVersion, HttpClient http, Func<IInstalledUpdater> createUpdater)
{
    private IInstalledUpdater? updater;
    private bool downloaded;
    private bool canInstall;
    public bool Enabled => options.Enabled;
    public string CurrentVersion => currentVersion;
    public Uri DownloadPage
    {
        get { options.EnsureEnabled(); return new Uri(options.DownloadPageUrl); }
    }

    public async Task<UpdateRelease?> CheckAsync()
    {
        options.EnsureEnabled();
        downloaded = false;
        canInstall = false;
        updater ??= createUpdater();
        if (updater.IsInstalled)
        {
            var release = await updater.CheckAsync();
            canInstall = release?.CanInstall == true;
            return release;
        }
        var json = await http.GetStringAsync(options.ManifestUrl);
        var manifest = JsonSerializer.Deserialize<Manifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is null || !SemanticVersion.TryParse(manifest.Version, out var latest) || latest.IsPrerelease)
            throw new InvalidDataException("The update manifest must contain a stable semantic version.");
        return latest > SemanticVersion.Parse(currentVersion)
            ? new UpdateRelease(latest.ToString(), manifest.ReleaseNotes ?? "", false) : null;
    }

    public async Task DownloadAsync()
    {
        options.EnsureEnabled();
        if (!canInstall || updater is null) throw new InvalidOperationException("Check for an installed update first.");
        await updater.DownloadAsync();
        downloaded = true;
    }

    public void Restart()
    {
        options.EnsureEnabled();
        if (!downloaded) throw new InvalidOperationException("Download an update before restarting.");
        updater!.Restart();
    }

    private sealed record Manifest(string Version, string? ReleaseNotes);
}

public sealed class VelopackUpdater(string feedUrl) : IInstalledUpdater
{
    private readonly UpdateManager manager = new(feedUrl);
    private UpdateInfo? pending;
    public bool IsInstalled => manager.IsInstalled;
    public async Task<UpdateRelease?> CheckAsync()
    {
        pending = null;
        pending = await manager.CheckForUpdatesAsync();
        return pending is null ? null : new(pending.TargetFullRelease.Version.ToString(), pending.TargetFullRelease.NotesMarkdown ?? "", true);
    }
    public Task DownloadAsync() => manager.DownloadUpdatesAsync(pending ?? throw new InvalidOperationException("Check for an update first."));
    public void Restart() => manager.ApplyUpdatesAndRestart(pending?.TargetFullRelease ?? throw new InvalidOperationException("Check for an update first."));
}
