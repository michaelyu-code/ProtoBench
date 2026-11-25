using ProtoBench.Application.Services;
using ProtoBench.Core.Documents;
using ProtoBench.Infrastructure.Configuration;

namespace ProtoBench.Application.Tests;

public sealed class TodoTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Current = new(2026, 11, 25, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Current;
        public void Advance(TimeSpan elapsed) => Current += elapsed;
    }
    private sealed class Store : ITodoStore
    {
        public TodoData Data = new();
        public bool Fail;
        public TodoData Load() => Data;
        public void Save(TodoData data)
        {
            if (Fail) throw new IOException("Unavailable");
            Data = data with { Items = data.Items.ToList() };
        }
    }
    [Fact]
    public void TimerExpiresOnceAndLeavesTaskOpenWithPersistentAttention()
    {
        var store = new Store(); var clock = new Clock(); var tasks = new TodoManager(store, clock); tasks.Load();
        tasks.Add("Check response mapping"); var id = tasks.Items[0].Id;
        tasks.SetDuration(id, 1); tasks.Start(id);
        var alerts = 0; tasks.TimerEnded += _ => alerts++;
        clock.Advance(TimeSpan.FromMinutes(3)); tasks.Tick(); tasks.Tick();
        Assert.Equal(1, alerts); Assert.Null(tasks.Get(id).CompletedAt); Assert.True(tasks.Get(id).NeedsAttention);
        Assert.Null(tasks.Get(id).Deadline); Assert.Equal(0, tasks.Remaining(tasks.Get(id)));
        var reopened = new TodoManager(store, clock); reopened.Load(); Assert.True(reopened.Get(id).NeedsAttention);
        reopened.Acknowledge(id); Assert.False(reopened.Get(id).NeedsAttention);
    }
    [Fact]
    public void StartingAnotherTaskPausesFirstAndResumePreservesTime()
    {
        var clock = new Clock(); var tasks = new TodoManager(new Store(), clock); tasks.Load();
        tasks.Add("First"); tasks.Add("Second"); var first = tasks.Items[0].Id; var second = tasks.Items[1].Id;
        tasks.Start(first); clock.Advance(TimeSpan.FromMinutes(5)); tasks.Start(second);
        Assert.Null(tasks.Get(first).Deadline); Assert.Equal(1200, tasks.Remaining(tasks.Get(first)));
        clock.Advance(TimeSpan.FromMinutes(2)); tasks.Start(first);
        Assert.Null(tasks.Get(second).Deadline); Assert.Equal(1380, tasks.Remaining(tasks.Get(second)));
        Assert.Equal(1200, tasks.Remaining(tasks.Get(first))); Assert.Single(tasks.Items, t => t.Deadline is not null);
    }
    [Fact]
    public void ExtendRestartCompleteAndReopenHaveIndependentMeaning()
    {
        var clock = new Clock(); var tasks = new TodoManager(new Store(), clock); tasks.Load(); tasks.Add("Task"); var id = tasks.Items[0].Id;
        tasks.SetDuration(id, 1); tasks.Start(id); clock.Advance(TimeSpan.FromMinutes(1)); tasks.Tick();
        tasks.Start(id, extend: true); Assert.Equal(300, tasks.Remaining(tasks.Get(id))); Assert.False(tasks.Get(id).NeedsAttention);
        tasks.Start(id, restart: true); Assert.Equal(60, tasks.Remaining(tasks.Get(id)));
        tasks.Complete(id, true); Assert.Null(tasks.Get(id).Deadline); Assert.NotNull(tasks.Get(id).CompletedAt);
        Assert.Throws<InvalidOperationException>(() => tasks.Start(id));
        tasks.Complete(id, false); Assert.Null(tasks.Get(id).CompletedAt);
    }
    [Fact]
    public void RetentionCountsFromCompletionAndReopeningCancelsRemoval()
    {
        var clock = new Clock(); var tasks = new TodoManager(new Store(), clock); tasks.Load();
        tasks.Add("Old"); var old = tasks.Items[0].Id; tasks.Complete(old, true);
        tasks.Add("Reopen"); var reopen = tasks.Items[1].Id; tasks.Complete(reopen, true);
        clock.Advance(TimeSpan.FromDays(13)); tasks.SetRetention(14); tasks.Tick(); Assert.Equal(2, tasks.Items.Count);
        tasks.Complete(reopen, false); clock.Advance(TimeSpan.FromDays(1)); tasks.Tick();
        Assert.Single(tasks.Items); Assert.Equal(reopen, tasks.Items[0].Id);
        tasks.Complete(reopen, true); clock.Advance(TimeSpan.FromDays(13)); tasks.Tick(); Assert.Single(tasks.Items);
        clock.Advance(TimeSpan.FromDays(1)); tasks.Tick(); Assert.Empty(tasks.Items);
    }
    [Fact]
    public void ForeverIsDefaultAndChangingRetentionRemovesAlreadyExpiredTasks()
    {
        var clock = new Clock(); var tasks = new TodoManager(new Store(), clock); tasks.Load(); tasks.Add("Keep"); tasks.Complete(tasks.Items[0].Id, true);
        clock.Advance(TimeSpan.FromDays(90)); tasks.Tick(); Assert.Single(tasks.Items); Assert.Null(tasks.RetentionDays);
        Assert.Equal(1, tasks.ExpiredCount(14)); tasks.SetRetention(14); Assert.Empty(tasks.Items);
    }
    [Fact]
    public void DeadlineSurvivesRestartAndSaveFailureDoesNotLoseTasksOrRepeatAlerts()
    {
        var store = new Store(); var clock = new Clock(); var tasks = new TodoManager(store, clock); tasks.Load(); tasks.Add("Task"); tasks.Start(tasks.Items[0].Id);
        tasks = new TodoManager(store, clock); tasks.Load(); var alerts = 0; tasks.TimerEnded += _ => alerts++;
        store.Fail = true; clock.Advance(TimeSpan.FromMinutes(25)); tasks.Tick(); tasks.Tick();
        Assert.Equal(1, alerts); Assert.NotEmpty(tasks.SaveError); Assert.Single(tasks.Items);
        store.Fail = false; tasks.RetrySave(); Assert.Empty(tasks.SaveError); Assert.True(store.Data.Items[0].NeedsAttention);
    }
    [Fact]
    public void JsonStoreRoundTripsAndPreservesMalformedFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProtoBench-todo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "tasks.json"); var store = new JsonTodoStore(path);
            var task = new TodoItem { Title = "Task", Steps = [new TodoStep(Guid.NewGuid(), "Verify")], Notes = "Notes", HighPriority = true };
            store.Save(new TodoData { Items = [task], RetentionDays = 14 });
            store.Save(store.Load()); var loaded = store.Load(); Assert.Equal(task.Title, loaded.Items[0].Title); Assert.Equal(14, loaded.RetentionDays); Assert.Single(loaded.Items[0].Steps);
            File.WriteAllText(path, "{ broken"); var manager = new TodoManager(store);
            Assert.ThrowsAny<Exception>(manager.Load); Assert.False(manager.IsLoaded); Assert.Throws<InvalidOperationException>(() => manager.Add("Task"));
            Assert.Equal("{ broken", File.ReadAllText(path)); Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
