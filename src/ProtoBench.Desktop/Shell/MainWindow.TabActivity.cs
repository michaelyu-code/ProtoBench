using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Workspaces;

namespace ProtoBench.Desktop.Shell;

public sealed partial class MainWindow
{
    private readonly Dictionary<ToolWorkspace, PropertyChangedEventHandler> tabActivityHandlers = [];
    private bool timerTaskbarIconActive;

    private void TrackTabActivity(TabViewItem tab, ToolWorkspace workspace)
    {
        if (workspace is not TimedKeyPressWorkspace timer) return;
        var title = tab.Header?.ToString() ?? workspace.Title;
        var idleIcon = tab.IconSource;
        var runningIcon = new FontIconSource { Glyph = "\uE768" }; // Play: visible even when a long tab title is trimmed.
        void Refresh()
        {
            tab.Header = timer.IsRunning ? $"{title} · Running" : title;
            tab.IconSource = timer.IsRunning ? runningIcon : idleIcon;
            ToolTipService.SetToolTip(tab, timer.IsRunning ? $"{title}\n{timer.Status}" : title);
            RefreshTimerTaskbarIcon();
        }
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(TimedKeyPressWorkspace.IsRunning) or nameof(ToolWorkspace.Status)) Refresh();
        };
        timer.PropertyChanged += handler;
        tabActivityHandlers.Add(workspace, handler);
        Refresh();
    }

    private void UntrackTabActivity(ToolWorkspace workspace)
    {
        if (!tabActivityHandlers.Remove(workspace, out var handler)) return;
        workspace.PropertyChanged -= handler;
        RefreshTimerTaskbarIcon();
    }

    private void RefreshTimerTaskbarIcon()
    {
        if (windowClosed) return;
        // A background timer still counts; closing/stopping one must not hide another.
        var running = tabActivityHandlers.Keys.OfType<TimedKeyPressWorkspace>().Any(timer => timer.IsRunning);
        if (running == timerTaskbarIconActive) return;
        try
        {
            var icon = running ? "ProtoBench.TimerRunning.ico" : "ProtoBench.ico";
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", icon));
            timerTaskbarIconActive = running;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update the timer taskbar icon");
        }
    }
}
