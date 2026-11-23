using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using ProtoBench.PluginSdk;

namespace ProtoBench.Infrastructure.Plugins;

public sealed record PluginUpdateRelease(string Id, string Version, int ApiVersion, string PackageUrl, string Sha256,
    string? ReleaseNotes = null);

/// <summary>Checks developer feeds without loading plugin code; downloads use the normal staged importer.</summary>
public sealed class PluginUpdateService(PluginPackageManager packages, HttpClient http,
    Func<string, Uri, string?>? getToken = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Redirects must be handled here so custom authentication headers cannot follow them to another host.
    public static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromMinutes(5) };

    public async Task<PluginUpdateRelease?> CheckAsync(InstalledPlugin plugin, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        cancellationToken = timeout.Token;
        var manifest = plugin.Manifest ?? throw new InvalidDataException("Plugin manifest is unavailable.");
        if (manifest.UpdateUrl is null) throw new InvalidOperationException("This plugin does not support update checks.");
        var source = PluginManifest.ValidateUpdateUrl(manifest.UpdateUrl);
        using var response = await GetAsync(manifest.UpdateUrl, source, getToken?.Invoke(manifest.Id, source), cancellationToken);
        using var data = new MemoryStream();
        await CopyBoundedAsync(response.Content, data, 65536, cancellationToken);
        var release = JsonSerializer.Deserialize<PluginUpdateRelease>(data.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("Empty plugin update feed.");
        ValidateRelease(release, manifest.Id);
        return NumericVersion(release.Version) > NumericVersion(plugin.PendingVersion ?? manifest.Version) ? release : null;
    }

    public async Task DownloadAsync(PluginUpdateRelease release, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        cancellationToken = timeout.Token;
        var installed = packages.Installed().SingleOrDefault(p => p.Manifest?.Id == release.Id)
            ?? throw new InvalidOperationException("Plugin is no longer installed.");
        ValidateRelease(release, installed.Manifest!.Id);
        if (NumericVersion(release.Version) <= NumericVersion(installed.PendingVersion ?? installed.Manifest.Version))
            throw new InvalidOperationException("This version is already installed or awaiting reload.");
        var temporary = Path.Combine(Path.GetTempPath(), "protobench-plugin-" + Guid.NewGuid().ToString("N") + ".nstplugin");
        var source = installed.Manifest.UpdateUrl is { } updateUrl ? PluginManifest.ValidateUpdateUrl(updateUrl) : null;
        var token = source is null ? null : getToken?.Invoke(installed.Manifest.Id, source);
        try
        {
            using (var response = await GetAsync(release.PackageUrl, source, token, cancellationToken))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await CopyBoundedAsync(response.Content, output, PluginPackageManager.MaxPackageBytes, cancellationToken);
            await using (var input = File.OpenRead(temporary))
            {
                var hash = await SHA256.HashDataAsync(input, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(release.Sha256)))
                    throw new InvalidDataException("Plugin download checksum does not match the update feed.");
            }
            var manifest = packages.Inspect(temporary);
            if (manifest.Id != release.Id || NumericVersion(manifest.Version) != NumericVersion(release.Version) || manifest.ApiVersion != release.ApiVersion)
                throw new InvalidDataException("Downloaded plugin does not match the advertised ID, version or SDK API.");
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Run(() => packages.Import(temporary), cancellationToken);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <remarks>The injected HTTP client must have automatic redirects disabled; use CreateHttpClient in production.</remarks>
    private async Task<HttpResponseMessage> GetAsync(string url, Uri? source, string? token, CancellationToken cancellationToken)
    {
        var uri = PluginManifest.ValidateUpdateUrl(url);
        if (token is not null && (token.Length > 4096 || token.Any(c => c < 33 || c > 126)))
            throw new InvalidOperationException("The saved GitLab token is invalid. Replace it in Plugin manager.");
        for (var redirects = 0; ; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (!string.IsNullOrEmpty(token) && source is not null && SameOrigin(uri, source))
                request.Headers.Add("PRIVATE-TOKEN", token);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            try
            {
                // Guard against accidentally supplying an automatically redirecting client.
                var final = PluginManifest.ValidateUpdateUrl(response.RequestMessage?.RequestUri?.AbsoluteUri ?? uri.AbsoluteUri);
                if (final != uri) throw new InvalidOperationException("Plugin update HTTP client must disable automatic redirects.");
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (redirects >= 5 || response.Headers.Location is not { } location)
                        throw new InvalidDataException("Plugin update has too many redirects or no redirect location.");
                    uri = PluginManifest.ValidateUpdateUrl(new Uri(uri, location).AbsoluteUri);
                    response.Dispose();
                    continue;
                }
                if ((int)response.StatusCode is 401 or 403 or 404)
                    throw new HttpRequestException($"Plugin update request returned HTTP {(int)response.StatusCode}. For a private GitLab project, save or replace its access token in Plugin manager and check the project URL and token permissions.");
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch { response.Dispose(); throw; }
        }
    }

    private static bool SameOrigin(Uri first, Uri second) => first.Scheme == second.Scheme &&
        first.Host.Equals(second.Host, StringComparison.OrdinalIgnoreCase) && first.Port == second.Port;

    private static void ValidateRelease(PluginUpdateRelease release, string id)
    {
        if (release.Id != id || !Version.TryParse(release.Version, out _) || release.ApiVersion != PluginApi.Version ||
            release.Sha256 is not { Length: 64 } || release.Sha256.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Update feed needs the matching plugin ID, a numeric version, compatible SDK API and SHA-256 checksum.");
        PluginManifest.ValidateUpdateUrl(release.PackageUrl);
    }

    private static Version NumericVersion(string value)
    {
        var version = Version.Parse(value);
        return new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }

    private static async Task CopyBoundedAsync(HttpContent content, Stream output, long limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > limit) throw new InvalidDataException("Plugin update download exceeds the size limit.");
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("Plugin update download exceeds the size limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (content.Headers.ContentLength is { } expected && total != expected)
            throw new InvalidDataException("Plugin update download is incomplete.");
    }
}
