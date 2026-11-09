using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NonStopToolkit.PluginSdk;

namespace NonStopToolkit.Infrastructure.Plugins;

public sealed record PluginManifest(int SchemaVersion, int ApiVersion, string Id, string Name, string Version,
    string EntryAssembly, string EntryType, string[] Files)
{
    public static PluginManifest Parse(string json)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Empty plugin manifest.");
        if (manifest.SchemaVersion != 1 || manifest.ApiVersion != PluginApi.Version)
            throw new InvalidDataException("Unsupported plugin schema or SDK API version.");
        if (manifest.Id is null || !Regex.IsMatch(manifest.Id, @"^[a-z0-9][a-z0-9.-]{0,79}$", RegexOptions.CultureInvariant) ||
            manifest.Id.EndsWith('.') || string.IsNullOrWhiteSpace(manifest.Name) || !System.Version.TryParse(manifest.Version, out _) ||
            string.IsNullOrWhiteSpace(manifest.EntryType))
            throw new InvalidDataException("Plugin needs a safe ID, name, numeric version and entry type.");
        if (manifest.Files is not { Length: > 0 and <= 512 } ||
            manifest.Files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Length)
            throw new InvalidDataException("Plugin files must contain 1–512 unique relative paths.");
        foreach (var file in manifest.Files) ValidateRelativePath(file);
        ValidateRelativePath(manifest.EntryAssembly);
        if (!manifest.EntryAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !manifest.Files.Contains(manifest.EntryAssembly, StringComparer.Ordinal))
            throw new InvalidDataException("EntryAssembly must be a DLL included in Files.");
        if (manifest.Files.Any(f => f.Equals("plugin.json", StringComparison.OrdinalIgnoreCase) ||
            f.Split('/').Any(part => part.StartsWith('.'))))
            throw new InvalidDataException("Reserved package file name.");
        return manifest;
    }

    public static void ValidateRelativePath(string path)
    {
        // Use a portable, Windows-safe package path grammar, not platform-dependent ZIP extraction rules.
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Contains('\\') ||
            path.Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
                p.Any(c => c < 32 || "<>:\"|?*".Contains(c)) ||
                Regex.IsMatch(p, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
            throw new InvalidDataException("Unsafe relative package path.");
    }
}

public sealed record InstalledPlugin(string Directory, PluginManifest? Manifest, bool Enabled, string? Error);
