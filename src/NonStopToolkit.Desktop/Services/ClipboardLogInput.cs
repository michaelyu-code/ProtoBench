using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace NonStopToolkit.Desktop.Services;

/// <summary>Copies clipboard UTF-16 to a seekable UTF-8 spool without creating a whole-log string or TextBox.</summary>
internal static class ClipboardLogInput
{
    public static Task<Stream> OpenAsync(CancellationToken token) => Task.Run<Stream>(() =>
    {
        token.ThrowIfCancellationRequested();
        bool opened = false;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (OpenClipboard(IntPtr.Zero)) { opened = true; break; }
            token.WaitHandle.WaitOne(20);
        }
        if (!opened) throw new Win32Exception(Marshal.GetLastWin32Error(), "Clipboard is busy. Try pasting again.");
        try
        {
            var handle = GetClipboardData(13); // CF_UNICODETEXT; ownership remains with Windows.
            if (handle == IntPtr.Zero) throw new InvalidDataException("The clipboard does not contain log text.");
            long bytes = checked((long)GlobalSize(handle).ToUInt64());
            if (bytes < 2) throw new InvalidDataException("The clipboard is empty.");
            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Clipboard text could not be read.");
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "nonstop-clipboard-" + Guid.NewGuid().ToString("N") + ".tmp");
                var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536,
                    FileOptions.SequentialScan | FileOptions.DeleteOnClose);
                try
                {
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true), 65536, leaveOpen: true))
                    {
                        var chars = new char[32768];
                        bool terminated = false;
                        for (long offset = 0; offset + 1 < bytes;)
                        {
                            token.ThrowIfCancellationRequested();
                            int count = (int)Math.Min(chars.Length, (bytes - offset) / 2);
                            Marshal.Copy(new IntPtr(checked(pointer.ToInt64() + offset)), chars, 0, count);
                            var end = Array.IndexOf(chars, '\0', 0, count);
                            writer.Write(chars, 0, end >= 0 ? end : count);
                            if (end >= 0) { terminated = true; break; }
                            offset += count * 2L;
                        }
                        if (!terminated) throw new InvalidDataException("Clipboard text is missing its terminating character.");
                    }
                    token.ThrowIfCancellationRequested();
                    if (stream.Length == 0) throw new InvalidDataException("The clipboard is empty.");
                    stream.Position = 0;
                    return stream;
                }
                catch { stream.Dispose(); throw; }
            }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }, token);

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(IntPtr handle);
}
