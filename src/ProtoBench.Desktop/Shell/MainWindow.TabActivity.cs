using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Workspaces;

namespace ProtoBench.Desktop.Shell;

public sealed partial class MainWindow
{
    private readonly Dictionary<ToolWorkspace, PropertyChangedEventHandler> tabActivityHandlers = [];

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
        if (tabActivityHandlers.Remove(workspace, out var handler)) workspace.PropertyChanged -= handler;
    }
}
