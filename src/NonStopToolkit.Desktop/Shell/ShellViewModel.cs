using CommunityToolkit.Mvvm.ComponentModel;
using NonStopToolkit.Application.Tools;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Desktop.Services;
namespace NonStopToolkit.Desktop.Shell;

public sealed class ShellViewModel(IToolRegistry tools, WorkspaceManager workspaces, SessionSettings settings) : ObservableObject
{
    public IToolRegistry Tools { get; } = tools;
    public WorkspaceManager Workspaces { get; } = workspaces;
    public SessionSettings Settings { get; } = settings;
    private string query = "";
    public string Query
    {
        get => query; set => SetProperty(ref query, value);
    }
    public IEnumerable<ToolDescriptor> FilteredTools => Tools.Tools.Where(t => (t.Name + " " + t.Description + " " + t.Category.DisplayName()).Contains(Query, StringComparison.OrdinalIgnoreCase));
}
