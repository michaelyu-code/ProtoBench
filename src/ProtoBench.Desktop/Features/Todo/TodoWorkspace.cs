using ProtoBench.Application.Services;
using ProtoBench.Application.Workspaces;

namespace ProtoBench.Desktop.Features.Todo;

public sealed class TodoWorkspace(TodoManager tasks) : ToolWorkspace
{
    public override string ToolId => "todo";
    public override string Title => "To-do";
    public TodoManager Tasks { get; } = tasks;
    public Guid? SelectedTask { get; set; }
    internal event Action? Closing;
    public override void Dispose() { Closing?.Invoke(); base.Dispose(); }
}
