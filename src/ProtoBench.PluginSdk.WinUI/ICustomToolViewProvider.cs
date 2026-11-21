using Microsoft.UI.Xaml;

namespace ProtoBench.PluginSdk.WinUI;

/// <summary>Optional advanced interface on the package entry type. Called on the UI thread.</summary>
public interface ICustomToolViewProvider
{
    /// <summary>
    /// Return null to use the standard responsive form. The host owns and disposes the session.
    /// Custom views own scrolling and reflow: support narrow content (approximately 320 DIPs),
    /// short viewports and Windows text scaling without clipping actions. Keep control state
    /// when resizing; use local horizontal scrolling only for inherently wide data.
    /// </summary>
    FrameworkElement? CreateView(string toolId, IPluginToolSession session);
}
