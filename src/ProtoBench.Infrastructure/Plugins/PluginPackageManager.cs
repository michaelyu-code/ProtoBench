using System.IO.Compression;

namespace ProtoBench.Infrastructure.Plugins;

/// <summary>Import never executes plugin code. Packages become active on the next app start.</summary>
public sealed class PluginPackageManager(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    public const long MaxPackageBytes = 128L * 1024 * 1024;

    public IReadOnlyList<InstalledPlugin> Installed()
    {
        Directory.CreateDirectory(Root);
        var result = new List<InstalledPlugin>();
        foreach (var directory in Directory.EnumerateDirectories(Root).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Path.GetFileName(directory).StartsWith('.')) continue;
            try
            {
                RejectLinks(directory);
                var manifest = ReadManifestFile(Path.Combine(directory, "plugin.json"));
                if (!Path.GetFileName(directory).Equals(manifest.Id, StringComparison.Ordinal))
                    throw new InvalidDataException("Plugin folder does not match its ID.");
                result.Add(new(directory, manifest, !File.Exists(Path.Combine(directory, ".disabled")), null));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
            { result.Add(new(directory, null, false, ex.Message)); }
        }
        return result;
    }

    public PluginManifest Inspect(string source)
    {
        source = Path.GetFullPath(source);
        if (IsArchive(source))
        {
            if (new FileInfo(source).Length > MaxPackageBytes) throw new InvalidDataException("Plugin package exceeds 128 MiB.");
            using var archive = ZipFile.OpenRead(source);
            var entry = archive.GetEntry("plugin.json") ?? throw new InvalidDataException("Package needs plugin.json at its root.");
            if (entry.Length > 65536) throw new InvalidDataException("Plugin manifest exceeds 64 KiB.");
            using var reader = new StreamReader(entry.Open());
            return PluginManifest.Parse(reader.ReadToEnd());
        }
        return ReadManifestFile(ManifestPath(source));
    }

    public PluginManifest Import(string source)
    {
        source = Path.GetFullPath(source);
        var manifest = Inspect(source);
        Directory.CreateDirectory(Root);
        var destination = Path.Combine(Root, manifest.Id);
        if (Directory.Exists(destination)) throw new InvalidDataException("This plugin ID is already installed. Remove its folder after closing the app before installing a replacement.");
        var staging = Path.Combine(Root, ".import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            long total = 0;
            if (IsArchive(source))
            {
                using var archive = ZipFile.OpenRead(source);
                var allowed = manifest.Files.Append("plugin.json").ToHashSet(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    // Directory entries are optional; validate them too, without extracting them.
                    PluginManifest.ValidateRelativePath(entry.FullName.TrimEnd('/'));
                    if (entry.FullName.EndsWith('/')) continue;
                    if (!seen.Add(entry.FullName) || !allowed.Contains(entry.FullName))
                        throw new InvalidDataException("Package has duplicate or undeclared files.");
                    total += entry.Length;
                    if (total > MaxPackageBytes || entry.Length < 0) throw new InvalidDataException("Expanded plugin exceeds 128 MiB.");
                    var target = PackagePath(staging, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    using var input = entry.Open();
                    using var output = File.Create(target);
                    CopyBounded(input, output, entry.Length);
                }
                if (!seen.SetEquals(allowed)) throw new InvalidDataException("Package is missing declared files.");
            }
            else
            {
                var sourceRoot = Path.GetDirectoryName(ManifestPath(source))!;
                foreach (var relative in manifest.Files.Append("plugin.json"))
                {
                    var input = PackagePath(sourceRoot, relative);
                    RejectLinks(input);
                    total += new FileInfo(input).Length;
                    if (total > MaxPackageBytes) throw new InvalidDataException("Plugin exceeds 128 MiB.");
                    var target = PackagePath(staging, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(input, target);
                }
            }
            // Re-read the staged manifest before committing; inspect/import never load the DLL.
            var stagedManifest = ReadManifestFile(Path.Combine(staging, "plugin.json"));
            if (stagedManifest != manifest && (System.Text.Json.JsonSerializer.Serialize(stagedManifest) != System.Text.Json.JsonSerializer.Serialize(manifest)))
                throw new InvalidDataException("Manifest changed during import.");
            Directory.Move(staging, destination);
            return manifest;
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    public void SetEnabled(string id, bool enabled)
    {
        var plugin = Installed().SingleOrDefault(p => p.Manifest?.Id == id) ?? throw new KeyNotFoundException("Plugin not installed.");
        var marker = Path.Combine(plugin.Directory, ".disabled");
        if (enabled) File.Delete(marker); else File.WriteAllText(marker, "Disabled; takes effect on next app start.");
    }

    internal static string PackagePath(string directory, string relative)
    {
        PluginManifest.ValidateRelativePath(relative);
        var rootPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(rootPath, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escapes package.");
        return full;
    }

    internal static void RejectLinks(string path)
    {
        FileSystemInfo initial = Directory.Exists(path) ? new DirectoryInfo(Path.GetFullPath(path)) : new FileInfo(Path.GetFullPath(path));
        for (var current = initial; current is not null; current = Directory.GetParent(current.FullName))
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Plugin files cannot use symbolic links or junctions.");
    }

    private static PluginManifest ReadManifestFile(string path)
    {
        RejectLinks(path);
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Plugin manifest exceeds 64 KiB.");
        return PluginManifest.Parse(File.ReadAllText(path));
    }
    private static string ManifestPath(string source) => source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        ? Path.Combine(Path.GetDirectoryName(source)!, "plugin.json") :
        Path.GetFileName(source).Equals("plugin.json", StringComparison.OrdinalIgnoreCase) ? source :
        throw new InvalidDataException("Select a .nstplugin/.zip package, plugin.json, or a DLL beside plugin.json.");
    private static bool IsArchive(string source) => source.EndsWith(".nstplugin", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    private static void CopyBounded(Stream input, Stream output, long expected)
    {
        var buffer = new byte[81920];
        long count = 0;
        int read;
        while ((read = input.Read(buffer)) != 0)
        {
            count += read;
            if (count > expected || count > MaxPackageBytes) throw new InvalidDataException("Expanded file exceeds its declared size.");
            output.Write(buffer, 0, read);
        }
        if (count != expected) throw new InvalidDataException("Incomplete package file.");
    }
}
