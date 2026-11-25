namespace ProtoBench.Core.Documents;

public sealed record TodoStep(Guid Id, string Text, bool Completed = false);
public sealed record TodoItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public string Notes { get; init; } = "";
    public DateOnly? DueDate { get; init; }
    public bool HighPriority { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public List<TodoStep> Steps { get; init; } = [];
    public int TimeboxMinutes { get; init; } = 25;
    public double RemainingSeconds { get; init; } = 1500;
    public DateTimeOffset? Deadline { get; init; }
    public bool NeedsAttention { get; init; }
}
public sealed record TodoData
{
    public int SchemaVersion { get; init; } = 1;
    public List<TodoItem> Items { get; init; } = [];
    // Null means keep forever; otherwise count from completion, never the due date.
    public int? RetentionDays { get; init; }
}
public interface ITodoStore
{
    TodoData Load();
    void Save(TodoData data);
}
