using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using NonStopToolkit.Application.Workspaces;
namespace NonStopToolkit.Desktop.Services;

public sealed class WorkspaceFactory<T>(string toolId, IServiceProvider services) : IToolWorkspaceFactory where T : ToolWorkspace
{
    public string ToolId => toolId;
    // WorkspaceManager owns disposal; the root provider must not retain closed transient tabs.
    public ToolWorkspace Create() => ActivatorUtilities.CreateInstance<T>(services);
}
public interface IWorkspaceViewFactory
{
    string ToolId
    {
        get;
    }
    FrameworkElement Create(ToolWorkspace workspace);
}
