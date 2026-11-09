using Microsoft.UI.Xaml;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Services;
namespace ProtoBench.Desktop.Features.Parsers;

public sealed class ParserViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "log-parser";
    public FrameworkElement Create(ToolWorkspace workspace) => new ParserView((ParserWorkspace)workspace, dialogs);
}
