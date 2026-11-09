using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
namespace NonStopToolkit.Application.Workspaces;

public abstract class ToolWorkspace : ObservableObject, IDisposable
{
    public Guid InstanceId { get; } = Guid.NewGuid();
    public abstract string ToolId
    {
        get;
    }
    public abstract string Title
    {
        get;
    }
    private bool busy;
    public bool IsBusy
    {
        get => busy; protected set => SetProperty(ref busy, value);
    }
    private string status = "Ready";
    public string Status
    {
        get => status; protected set => SetProperty(ref status, value);
    }
    private CancellationTokenSource? operation;
    public void Cancel() => operation?.Cancel();
    protected async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy)
            return;
        using var source = new CancellationTokenSource();
        operation = source;
        IsBusy = true;
        Status = "Working…";
        try
        {
            await action(source.Token);
            Status = "Completed";
        }
        catch (OperationCanceledException) { Status = "Cancelled"; }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; operation = null; }
    }
    public virtual void Dispose()
    {
        Cancel();
    }
}
public interface IToolWorkspaceFactory
{
    string ToolId
    {
        get;
    }
    ToolWorkspace Create();
}
public sealed class WorkspaceManager(IEnumerable<IToolWorkspaceFactory> factories) : IDisposable
{
    private readonly IReadOnlyDictionary<string, IToolWorkspaceFactory> factories = factories.ToDictionary(f => f.ToolId, StringComparer.OrdinalIgnoreCase);
    public ObservableCollection<ToolWorkspace> Workspaces { get; } = [];
    public ToolWorkspace Open(string id)
    {
        var workspace = factories[id].Create();
        Workspaces.Add(workspace);
        return workspace;
    }
    public void Close(ToolWorkspace workspace)
    {
        if (Workspaces.Remove(workspace))
            workspace.Dispose();
    }
    public void Dispose()
    {
        foreach (var workspace in Workspaces.ToArray())
            Close(workspace);
    }
}
