using ProtoBench.Core.Documents;

namespace ProtoBench.Application.Services;

/// <summary>Shared task collection. Call from the owning UI thread; time is injectable for deterministic checks.</summary>
public sealed class TodoManager(ITodoStore store, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private TodoData data = new();
    public IReadOnlyList<TodoItem> Items => data.Items.AsReadOnly();
    public int? RetentionDays => data.RetentionDays;
    public bool IsLoaded { get; private set; }
    public string SaveError { get; private set; } = "";
    public event Action? Changed;
    public event Action<TodoItem>? TimerEnded;
    public DateTimeOffset Now => clock.GetUtcNow();
    public void Load()
    {
        if (IsLoaded) return;
        data = store.Load(); // A malformed file must never be overwritten with an empty collection.
        IsLoaded = true;
        Tick();
        Changed?.Invoke();
    }
    private void Publish()
    {
        try { store.Save(data); SaveError = ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { SaveError = "Tasks could not be saved. Changes remain in memory. Retry saving before closing the app."; }
        Changed?.Invoke();
    }
    public void RetrySave() { EnsureLoaded(); Publish(); }
    private void EnsureLoaded()
    {
        if (!IsLoaded) throw new InvalidOperationException("Task storage is not loaded. Correct the task file and restart ProtoBench.");
    }
    public TodoItem Get(Guid id) => data.Items.First(t => t.Id == id);
    public double Remaining(TodoItem item) => Math.Max(0, item.Deadline is { } end ? (end - Now).TotalSeconds : item.RemainingSeconds);
    public void Add(string title)
    {
        EnsureLoaded();
        title = title.Trim();
        if (title.Length is < 1 or > 200) throw new ArgumentException("Enter a task title of 1–200 characters.");
        data.Items.Add(new TodoItem { Title = title, DueDate = DateOnly.FromDateTime(clock.GetLocalNow().DateTime) });
        Publish();
    }
    public void Update(TodoItem item)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 200 || item.Notes.Length > 10000)
            throw new ArgumentException("A task needs a title of 1–200 characters; notes are limited to 10,000 characters.");
        if (item.HighPriority && item.LowPriority || !Enum.IsDefined(item.CardColor))
            throw new ArgumentException("Choose one priority and a supported card colour.");
        var index = data.Items.FindIndex(t => t.Id == item.Id);
        if (index < 0) throw new InvalidOperationException("The task was removed in another tab.");
        data.Items[index] = item;
        Publish();
    }
    public void Complete(Guid id, bool completed)
    {
        var t = Get(id);
        Update(t with { CompletedAt = completed ? Now : null, RemainingSeconds = Remaining(t), Deadline = null, NeedsAttention = false });
    }
    public void SetDuration(Guid id, int minutes)
    {
        if (minutes is < 1 or > 480) throw new ArgumentException("Choose a timebox from 1 to 480 minutes.");
        var t = Get(id);
        Update(t with { TimeboxMinutes = minutes, RemainingSeconds = minutes * 60, Deadline = null, NeedsAttention = false });
    }
    public void Start(Guid id, bool restart = false, bool extend = false)
    {
        EnsureLoaded();
        Tick();
        var task = Get(id);
        if (task.CompletedAt is not null) throw new InvalidOperationException("Reopen the task before starting a timer.");
        data = data with { Items = data.Items.Select(t => t.Deadline is not null
            ? t with { RemainingSeconds = Remaining(t), Deadline = null } : t).ToList() };
        task = Get(id);
        var seconds = extend ? Remaining(task) + 300 : restart || Remaining(task) <= 0 ? task.TimeboxMinutes * 60 : Remaining(task);
        Update(task with { Deadline = Now.AddSeconds(seconds), RemainingSeconds = seconds, NeedsAttention = false });
    }
    public void Pause(Guid id)
    {
        Tick();
        var t = Get(id);
        Update(t with { RemainingSeconds = Remaining(t), Deadline = null });
    }
    public void Reset(Guid id) { var t = Get(id); SetDuration(id, t.TimeboxMinutes); }
    public void Acknowledge(Guid id) { var t = Get(id); Update(t with { NeedsAttention = false }); }
    public void Remove(Guid id)
    {
        EnsureLoaded();
        data.Items.RemoveAll(t => t.Id == id);
        Publish();
    }
    public int ExpiredCount(int days) => data.Items.Count(t => t.CompletedAt is { } at && Now - at >= TimeSpan.FromDays(days));
    public void SetRetention(int? days)
    {
        EnsureLoaded();
        if (days is < 1 or > 3650) throw new ArgumentException("Retention must be 1–3650 days, or forever.");
        data = data with { RetentionDays = days };
        if (days is { } n) data.Items.RemoveAll(t => t.CompletedAt is { } at && Now - at >= TimeSpan.FromDays(n));
        Publish();
    }
    public void Tick()
    {
        if (!IsLoaded) return;
        var ended = data.Items.Where(t => t.CompletedAt is null && t.Deadline <= Now).ToArray();
        foreach (var t in ended)
        {
            var index = data.Items.FindIndex(x => x.Id == t.Id);
            data.Items[index] = t with { Deadline = null, RemainingSeconds = 0, NeedsAttention = true };
        }
        var removed = data.RetentionDays is { } days
            ? data.Items.RemoveAll(t => t.CompletedAt is { } at && Now - at >= TimeSpan.FromDays(days)) : 0;
        if (ended.Length > 0 || removed > 0) Publish();
        foreach (var t in ended) TimerEnded?.Invoke(Get(t.Id));
    }
}
