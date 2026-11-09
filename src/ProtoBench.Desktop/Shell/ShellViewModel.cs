using CommunityToolkit.Mvvm.ComponentModel;
using ProtoBench.Application.Tools;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Services;
namespace ProtoBench.Desktop.Shell;

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
