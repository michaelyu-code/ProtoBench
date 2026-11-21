using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using ProtoBench.Infrastructure.Configuration;

namespace ProtoBench.Desktop.Services;

internal static class LocalDataRemoval
{
    private const string Argument = "--remove-local-data";

    public static void StartAfterExit()
    {
        EnsureNoOtherInstances(Environment.ProcessId);
        using var current = Process.GetCurrentProcess();
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Could not locate ProtoBench's executable.");
        if (!Path.GetFileNameWithoutExtension(executable).Equals("ProtoBench", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Open ProtoBench.exe to use local data removal.");
        var data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppPaths.UserDirectory));
        if (Path.GetFullPath(AppContext.BaseDirectory).StartsWith(data + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Move the portable app outside its local data folder before removing local data.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(current.Id.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture));
        using var helper = Process.Start(start) ?? throw new IOException("Could not start local data cleanup. No data was removed.");
    }

    // This runs before logging, plugin loading and WinUI so cleanup cannot recreate user data.
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != Argument) return false;
        try
        {
            if (args.Length != 3 || !int.TryParse(args[1], CultureInfo.InvariantCulture, out var parentId) ||
                !long.TryParse(args[2], CultureInfo.InvariantCulture, out var parentStarted) || parentId == Environment.ProcessId)
                throw new ArgumentException("Invalid local data cleanup request.");
            WaitForParent(parentId, parentStarted);
            EnsureNoOtherInstances(Environment.ProcessId);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            for (var attempt = 0; ; attempt++)
            {
                try { LocalDataRemover.Remove(local); break; }
                catch (Exception ex) when (attempt < 9 && ex is IOException or UnauthorizedAccessException)
                { Thread.Sleep(300); }
            }
            ShowMessage("ProtoBench's local settings, plugins, custom definitions and logs have been removed.\n\nProtoBench is still installed. You can now uninstall it through Windows Settings, or open it again with default settings.", error: false);
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            ShowMessage($"Local data could not be fully removed. Some files may remain.\n\n{ex.Message}\n\nClose all ProtoBench instances and try again. Local data folder:\n{AppPaths.UserDirectory}", error: true);
        }
        return true;
    }

    private static void WaitForParent(int id, long started)
    {
        Process parent;
        try { parent = Process.GetProcessById(id); }
        catch (ArgumentException) { return; } // Parent already exited.
        using (parent)
        {
            try
            {
                if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != started) return; // PID was reused.
                if (!parent.WaitForExit(30_000))
                    throw new IOException("ProtoBench is still closing. Its files have not been removed.");
            }
            catch (InvalidOperationException) when (parent.HasExited) { } // Exited while reading its start time.
        }
    }

    private static void EnsureNoOtherInstances(int ownId)
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
                if (process.Id != ownId && !process.HasExited)
                    throw new InvalidOperationException("Close other ProtoBench instances before removing local data.");
        }
    }

    private static void ShowMessage(string text, bool error) =>
        MessageBoxW(IntPtr.Zero, text, "ProtoBench local data", 0x10000u | (error ? 0x10u : 0x40u));

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
