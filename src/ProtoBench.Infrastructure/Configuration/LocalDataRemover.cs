namespace ProtoBench.Infrastructure.Configuration;

/// <summary>Removes only ProtoBench's child directory, without following directory links.</summary>
public static class LocalDataRemover
{
    public static string GetDirectory(string localApplicationDataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localApplicationDataDirectory);
        var parent = Path.GetFullPath(localApplicationDataDirectory);
        var target = Path.GetFullPath(Path.Combine(parent, "ProtoBench"));
        if (!string.Equals(Path.GetDirectoryName(target), Path.TrimEndingDirectorySeparator(parent),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The local data directory must be directly inside the application-data directory.");
        return target;
    }

    public static void Remove(string localApplicationDataDirectory)
    {
        var target = GetDirectory(localApplicationDataDirectory);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(target); }
        catch (DirectoryNotFoundException) { return; }
        catch (FileNotFoundException) { return; }
        if (!attributes.HasFlag(FileAttributes.Directory))
            throw new IOException("The ProtoBench local data path is not a directory.");
        RemoveTree(new DirectoryInfo(target));
    }

    private static void RemoveTree(DirectoryInfo directory)
    {
        // Delete junctions/symbolic links themselves; never enumerate their destinations.
        if (!directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.Directory)) RemoveTree(new DirectoryInfo(entry.FullName));
                else
                {
                    if (!entry.Attributes.HasFlag(FileAttributes.ReparsePoint) && entry.Attributes.HasFlag(FileAttributes.ReadOnly))
                        entry.Attributes &= ~FileAttributes.ReadOnly;
                    File.Delete(entry.FullName);
                }
            }
        }
        directory.Delete();
    }
}
