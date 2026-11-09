using Microsoft.UI.Xaml;

namespace ProtoBench.PluginSdk.WinUI;

/// <summary>Optional advanced interface on the package entry type. Called on the UI thread.</summary>
public interface ICustomToolViewProvider
{
    /// <summary>Return null to use the standard form. The host owns and disposes the session.</summary>
    FrameworkElement? CreateView(string toolId, IPluginToolSession session);
}
