using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using ProtoBench.Application.Workspaces;
using ProtoBench.PluginSdk;

namespace ProtoBench.Application.Tests;

public sealed class PluginCommandTests
{
    [Fact]
    public async Task PendingCommandsAreBoundedAndAllQueuedCommandsCancelOnClose()
    {
        var session = new CommandSession();
        using var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        var tasks = Enumerable.Range(0, 128).Select(i => bridge.ReceiveAsync(Command(i.ToString(), "work" + i))).ToArray();
        await session.Started();
        using var rejected = JsonDocument.Parse(await bridge.ReceiveAsync(Command("excess", "excess")));
        Assert.Equal("error", rejected.RootElement.GetProperty("type").GetString());
        Assert.Contains("128", rejected.RootElement.GetProperty("error").GetString());
        workspace.Dispose();
        await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(session.Calls);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task LegacySessionsRejectCommandsAndCommandSessionsRejectOverlapWithLegacyRun()
    {
        var legacy = new LegacySession();
        using var legacyWorkspace = Workspace(legacy);
        using var unsupported = JsonDocument.Parse(await new PluginWebViewBridge(legacyWorkspace).ReceiveAsync(Command("a", "test")));
        Assert.Contains("does not support", unsupported.RootElement.GetProperty("error").GetString());
        var session = new CommandSession { HoldForm = true };
        using var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        var form = bridge.ReceiveAsync("""{"type":"execute","id":"form","inputs":{}}""");
        await session.FormStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var busy = JsonDocument.Parse(await bridge.ReceiveAsync(Command("b", "test")));
        Assert.Equal("error", busy.RootElement.GetProperty("type").GetString());
        Assert.Empty(session.Calls);
        await bridge.ReceiveAsync("""{"type":"cancel","id":"cancel"}""");
        await form.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ParallelCommandsCompleteOutOfOrderWithoutChangingFormState()
    {
        var session = new CommandSession(PluginCommandConcurrency.Parallel);
        using var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        await workspace.ExecuteAsync();
        var first = bridge.ReceiveAsync(Command("a", "first", "one"));
        Assert.Equal("first", await session.Started());
        var second = bridge.ReceiveAsync(Command("b", "second", "two"));
        Assert.Equal("second", await session.Started());
        session.Release("second");
        using var secondReply = JsonDocument.Parse(await second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("b", secondReply.RootElement.GetProperty("id").GetString());
        Assert.Equal("two", secondReply.RootElement.GetProperty("result").GetProperty("value").GetString());
        Assert.False(first.IsCompleted);
        session.Release("first");
        using var firstReply = JsonDocument.Parse(await first);
        Assert.Equal("one", firstReply.RootElement.GetProperty("result").GetProperty("value").GetString());
        Assert.Equal("form", workspace.Output);
        Assert.Equal("initial", workspace.Inputs["value"]);
        Assert.False(workspace.IsBusy);
    }

    [Fact]
    public async Task SequentialCommandsQueueAndQueuedCancellationDoesNotInvokePlugin()
    {
        var session = new CommandSession();
        using var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        var first = bridge.ReceiveAsync(Command("a", "first"));
        Assert.Equal("first", await session.Started());
        var queued = bridge.ReceiveAsync(Command("b", "queued"));
        using var ack = JsonDocument.Parse(await bridge.ReceiveAsync("""{"type":"cancelCommand","id":"cancel","targetId":"b"}"""));
        Assert.True(ack.RootElement.GetProperty("cancelled").GetBoolean());
        using var cancelled = JsonDocument.Parse(await queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("Cancelled", cancelled.RootElement.GetProperty("status").GetString());
        Assert.False(session.Calls.ContainsKey("queued"));
        var next = bridge.ReceiveAsync(Command("c", "next"));
        session.Release("first");
        await first;
        Assert.Equal("next", await session.Started());
        session.Release("next");
        await next;
        Assert.Equal(1, session.MaximumActive);
    }

    [Fact]
    public async Task CancellingOneRunningCommandLeavesOtherCommandRunning()
    {
        var session = new CommandSession(PluginCommandConcurrency.Parallel);
        using var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        var first = bridge.ReceiveAsync(Command("a", "first"));
        await session.Started();
        var second = bridge.ReceiveAsync(Command("b", "second"));
        await session.Started();
        await bridge.ReceiveAsync("""{"type":"cancelCommand","id":"cancel","targetId":"a"}""");
        using var cancelled = JsonDocument.Parse(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(cancelled.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Cancelled", cancelled.RootElement.GetProperty("status").GetString());
        Assert.False(second.IsCompleted);
        session.Release("second");
        await second;
    }

    [Fact]
    public async Task DuplicateIdsAndLegacyExecutionAreRejectedWithoutMutation()
    {
        var session = new CommandSession(PluginCommandConcurrency.Parallel);
        using var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        var first = bridge.ReceiveAsync(Command("a", "first"));
        await session.Started();
        foreach (var message in new[] { Command("a", "duplicate"), """{"type":"execute","id":"form","inputs":{"value":"changed"}}""" })
        {
            using var reply = JsonDocument.Parse(await bridge.ReceiveAsync(message));
            Assert.Equal("error", reply.RootElement.GetProperty("type").GetString());
        }
        Assert.Equal("initial", workspace.Inputs["value"]);
        session.Release("first");
        await first;
        // IDs can be reused after the previous operation has completely drained.
        var reused = bridge.ReceiveAsync(Command("a", "reused"));
        Assert.Equal("reused", await session.Started());
        session.Release("reused");
        await reused;
    }

    [Fact]
    public async Task ClosingCancelsActiveAndQueuedCommandsAndWaitsBeforeDisposing()
    {
        var session = new CommandSession { HoldCancellation = true };
        var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        var running = bridge.ReceiveAsync(Command("a", "first"));
        await session.Started();
        var queued = bridge.ReceiveAsync(Command("b", "queued"));
        workspace.Dispose();
        await session.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, session.DisposeCount);
        using var late = JsonDocument.Parse(await bridge.ReceiveAsync(Command("c", "late")));
        Assert.False(late.RootElement.GetProperty("success").GetBoolean());
        session.AllowCancellation.SetResult();
        await Task.WhenAll(running, queued).WaitAsync(TimeSpan.FromSeconds(5));
        workspace.Dispose();
        Assert.Equal(1, session.DisposeCount);
        Assert.False(session.Calls.ContainsKey("queued"));
    }

    [Fact]
    public async Task ClosingWaitsForCancellationCallbacksToo()
    {
        var session = new CommandSession { HoldCallback = true };
        var workspace = Workspace(session);
        var running = new PluginWebViewBridge(workspace).ReceiveAsync(Command("a", "first"));
        await session.Started();
        var bridge = new PluginWebViewBridge(workspace);
        await bridge.ReceiveAsync("""{"type":"cancelCommand","id":"cancel","targetId":"a"}""");
        await session.CallbackSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await bridge.ReceiveAsync("""{"type":"cancelCommand","id":"again","targetId":"a"}""");
        workspace.Dispose();
        Assert.Equal(0, session.DisposeCount);
        session.AllowCallback.SetResult();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task EventsAreOrderedIsolatedAndStopAfterClosing()
    {
        var session = new CommandSession();
        var workspace = Workspace(session);
        using var other = Workspace(new CommandSession());
        var messages = new List<string>();
        workspace.WebMessageReceived += (_, message) => messages.Add(message);
        other.WebMessageReceived += (_, _) => throw new InvalidOperationException("Wrong tab");
        await Task.Run(() => { session.Publish("connected"); session.Publish("traffic"); });
        Assert.Equal(2, messages.Count);
        using var first = JsonDocument.Parse(messages[0]);
        using var second = JsonDocument.Parse(messages[1]);
        Assert.Equal("event", first.RootElement.GetProperty("type").GetString());
        Assert.Equal("connected", first.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, first.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(2, second.RootElement.GetProperty("sequence").GetInt64());
        workspace.Dispose();
        session.Publish("late");
        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public async Task CommandFailuresDoNotBlockSubsequentCommands()
    {
        var session = new CommandSession();
        using var workspace = Workspace(session);
        var bridge = new PluginWebViewBridge(workspace);
        using var failure = JsonDocument.Parse(await bridge.ReceiveAsync(Command("a", "fail")));
        Assert.False(failure.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Failed deliberately", failure.RootElement.GetProperty("status").GetString());
        var next = bridge.ReceiveAsync(Command("b", "next"));
        await session.Started();
        session.Release("next");
        using var result = JsonDocument.Parse(await next);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean());
    }

    [Theory]
    [InlineData("""{"type":"command","id":"a","command":"","arguments":{}}""")]
    [InlineData("""{"type":"command","id":"a","command":"test","arguments":[]}""")]
    [InlineData("""{"type":"command","id":"a","command":"test"}""")]
    [InlineData("""{"type":"cancelCommand","id":"a","targetId":""}""")]
    public async Task InvalidCommandsNeverReachSession(string message)
    {
        var session = new CommandSession();
        using var workspace = Workspace(session);
        using var response = JsonDocument.Parse(await new PluginWebViewBridge(workspace).ReceiveAsync(message));
        Assert.False(response.RootElement.GetProperty("success").GetBoolean());
        Assert.Empty(session.Calls);
    }

    private static PluginToolWorkspace Workspace(IPluginToolSession session) => new(new PluginTool("test.commands", "Commands", "", [new("value", "Value", DefaultValue: "initial")], () => session));
    private static string Command(string id, string command, string value = "test") => JsonSerializer.Serialize(new { type = "command", id, command, arguments = new { value } });

    private sealed class LegacySession : IPluginToolSession
    {
        public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token) => Task.FromResult(new PluginToolResult("legacy"));
        public void Dispose() { }
    }

    private sealed class CommandSession(PluginCommandConcurrency concurrency = PluginCommandConcurrency.Sequential) : IPluginCommandSession
    {
        public PluginCommandConcurrency CommandConcurrency => concurrency;
        public event EventHandler<PluginToolEvent>? EventReceived;
        private readonly Channel<string> started = Channel.CreateUnbounded<string>();
        public ConcurrentDictionary<string, TaskCompletionSource> Calls { get; } = new();
        public TaskCompletionSource CancellationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CallbackSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCallback { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldCancellation { get; init; }
        public bool HoldCallback { get; init; }
        public bool HoldForm { get; init; }
        public TaskCompletionSource FormStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount;
        private int active;
        public int MaximumActive;
        public async Task<string> Started() => await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public void Release(string command) => Calls[command].TrySetResult();
        public void Publish(string name) => EventReceived?.Invoke(this, new(name, JsonSerializer.SerializeToElement(new { value = "traffic" })));
        public async Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
        {
            FormStarted.TrySetResult();
            if (HoldForm) await Task.Delay(Timeout.Infinite, token);
            return new PluginToolResult("form");
        }
        public async Task<JsonElement> ExecuteCommandAsync(string command, JsonElement arguments, CancellationToken token)
        {
            if (command == "fail") throw new InvalidOperationException("Failed deliberately");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Calls[command] = release;
            var count = Interlocked.Increment(ref active);
            MaximumActive = Math.Max(MaximumActive, count);
            // Leave this registration attached so the host, rather than registration.Dispose,
            // must await the cancellation callback before disposing the session.
            if (HoldCallback) token.Register(() => { CallbackSeen.TrySetResult(); AllowCallback.Task.GetAwaiter().GetResult(); });
            started.Writer.TryWrite(command);
            try
            {
                await release.Task.WaitAsync(token);
                return arguments.Clone();
            }
            catch (OperationCanceledException)
            {
                CancellationSeen.TrySetResult();
                if (HoldCancellation) await AllowCancellation.Task;
                throw;
            }
            finally { Interlocked.Decrement(ref active); }
        }
        public void Dispose() => Interlocked.Increment(ref DisposeCount);
    }
}
