using System.Diagnostics;
using ProtoBench.Infrastructure.Configuration;

namespace ProtoBench.Application.Tests;

public sealed class LocalDataRemoverTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("protobench-cleanup-").FullName;
    private string Data => LocalDataRemover.GetDirectory(directory);

    [Fact]
    public void RemovesAllLocalDataAndPreservesSiblingFilesAndInstallation()
    {
        foreach (var relative in new[] { "settings.json", "plugins/example/Example.dll", "definitions/messages/custom.json", "logs/application.log", "other/backup.json" })
        {
            var file = Path.Combine(Data, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "test data");
        }
        File.SetAttributes(Path.Combine(Data, "plugins/example/Example.dll"), FileAttributes.ReadOnly);
        var saved = Path.Combine(directory, "saved.log");
        File.WriteAllText(saved, "saved input");
        var installation = Path.Combine(directory, "ProtoBench.Desktop");
        Directory.CreateDirectory(installation);
        File.WriteAllText(Path.Combine(installation, "ProtoBench.exe"), "app");
        LocalDataRemover.Remove(directory);
        Assert.False(Directory.Exists(Data));
        Assert.Equal("saved input", File.ReadAllText(saved));
        Assert.Equal("app", File.ReadAllText(Path.Combine(installation, "ProtoBench.exe")));
        LocalDataRemover.Remove(directory); // Already removed is a successful no-op.
    }

    [Fact]
    public void LockedFilesReportFailureAndCanBeRemovedAfterTheyClose()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Data);
        var file = Path.Combine(Data, "loaded-plugin.dll");
        using (var locked = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<IOException>(() => LocalDataRemover.Remove(directory));
            Assert.True(File.Exists(file));
        }
        LocalDataRemover.Remove(directory);
        Assert.False(Directory.Exists(Data));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotFollowDirectoryLinks(bool linkAtRoot)
    {
        var external = Path.Combine(directory, "outside-data");
        Directory.CreateDirectory(external);
        File.WriteAllText(Path.Combine(external, "keep.txt"), "keep");
        var link = linkAtRoot ? Data : Path.Combine(Data, "linked-folder");
        if (!linkAtRoot) Directory.CreateDirectory(Data);
        if (OperatingSystem.IsWindows())
        {
            // Junction creation does not require Developer Mode or symbolic-link privileges.
            var script = Path.Combine(directory, "create-junction.ps1");
            File.WriteAllText(script, "param([string]$LinkPath, [string]$TargetPath)\nNew-Item -ItemType Junction -Path $LinkPath -Target $TargetPath -ErrorAction Stop | Out-Null\n");
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-LinkPath", link, "-TargetPath", external })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(10_000));
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(link, external);
        Assert.True(File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint));
        LocalDataRemover.Remove(directory);
        Assert.False(Directory.Exists(Data));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(external, "keep.txt")));
    }

    [Fact]
    public void RejectsAnEmptyApplicationDataPath() =>
        Assert.Throws<ArgumentException>(() => LocalDataRemover.Remove(" "));

    public void Dispose()
    {
        var full = Path.GetFullPath(directory);
        if (Path.GetDirectoryName(full) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()) &&
            Path.GetFileName(full).StartsWith("protobench-cleanup-", StringComparison.Ordinal))
            Directory.Delete(full, recursive: true);
    }
}
