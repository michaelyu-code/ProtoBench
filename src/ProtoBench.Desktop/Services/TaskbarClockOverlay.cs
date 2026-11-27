using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace ProtoBench.Desktop.Services;

/// <summary>Uses the shell's status overlay, including for pinned taskbar buttons.</summary>
internal sealed class TaskbarClockOverlay : IDisposable
{
    private readonly IntPtr window;
    private readonly Action<Exception> reportError;
    private readonly SubclassProc procedure;
    private readonly uint taskbarButtonCreated;
    private ITaskbarList3? taskbar;
    private bool ready, running, disposed;
    private bool? applied;

    public TaskbarClockOverlay(Window owner, Action<Exception> reportError)
    {
        window = WinRT.Interop.WindowNative.GetWindowHandle(owner);
        this.reportError = reportError;
        procedure = WindowProcedure;
        taskbarButtonCreated = RegisterWindowMessage("TaskbarButtonCreated");
        if (taskbarButtonCreated == 0 || !SetWindowSubclass(window, procedure, 1, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void SetRunning(bool value)
    {
        running = value;
        Apply();
    }

    private void Apply()
    {
        if (disposed || !ready || applied == running) return;
        try
        {
            if (taskbar is null)
            {
                taskbar = (ITaskbarList3)Activator.CreateInstance(Type.GetTypeFromCLSID(
                    new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"), throwOnError: true)!)!;
                taskbar.HrInit();
            }
            var icon = IntPtr.Zero;
            try
            {
                if (running)
                {
                    icon = LoadImage(IntPtr.Zero, Path.Combine(AppContext.BaseDirectory, "Assets", "TimerClock.ico"),
                        1, 16, 16, 0x10); // IMAGE_ICON, LR_LOADFROMFILE; owned by this call.
                    if (icon == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                taskbar.SetOverlayIcon(window, icon, running ? "Timed Key Presser running" : "");
                applied = running;
            }
            finally
            {
                // Explorer copies the icon; the caller still owns the original HICON.
                if (icon != IntPtr.Zero) DestroyIcon(icon);
            }
        }
        catch (Exception ex) { reportError(ex); }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == taskbarButtonCreated)
        {
            // Also sent after Explorer restarts: replay the desired state even if it has not changed.
            ready = true;
            applied = null;
            Apply();
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        RemoveWindowSubclass(window, procedure, 1);
        if (taskbar is not null) Marshal.ReleaseComObject(taskbar);
        taskbar = null;
    }

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc procedure, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc procedure, UIntPtr id);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    // COM declarations must retain every inherited vtable slot before SetOverlayIcon.
    [ComImport, Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
        void SetProgressState(IntPtr hwnd, uint state);
        void RegisterTab(IntPtr tab, IntPtr parent);
        void UnregisterTab(IntPtr tab);
        void SetTabOrder(IntPtr tab, IntPtr before);
        void SetTabActive(IntPtr tab, IntPtr parent, uint reserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }
}
