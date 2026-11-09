using Microsoft.UI.Xaml;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Desktop.Services;
namespace NonStopToolkit.Desktop.Features.Parsers;

public sealed class ParserViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "log-parser";
    public FrameworkElement Create(ToolWorkspace workspace) => new ParserView((ParserWorkspace)workspace, dialogs);
}
