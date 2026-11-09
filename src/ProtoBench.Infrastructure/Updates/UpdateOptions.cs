using System.Reflection;

namespace ProtoBench.Infrastructure.Updates;

public sealed record UpdateOptions(bool Enabled = false, string ManifestUrl = "", string DownloadPageUrl = "", string FeedUrl = "")
{
    public static UpdateOptions FromAssembly(Assembly assembly)
    {
        var values = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value);
        return new(bool.TryParse(values.GetValueOrDefault("UpdatesEnabled"), out var enabled) && enabled,
            values.GetValueOrDefault("UpdateManifestUrl") ?? "",
            values.GetValueOrDefault("UpdateDownloadPageUrl") ?? "",
            values.GetValueOrDefault("UpdateFeedUrl") ?? "");
    }

    public void EnsureEnabled()
    {
        if (!Enabled) throw new InvalidOperationException("Updates are disabled for this build.");
        foreach (var value in new[] { ManifestUrl, DownloadPageUrl, FeedUrl })
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
                throw new InvalidOperationException("Updates require HTTPS manifest, download page and feed URLs.");
    }
}
