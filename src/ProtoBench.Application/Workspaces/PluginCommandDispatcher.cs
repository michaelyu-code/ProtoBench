using System.Text.Json;
using ProtoBench.PluginSdk;

namespace ProtoBench.Application.Workspaces;

/// <summary>Owns command lifetimes separately from the form's single execution state.</summary>
internal sealed class PluginCommandDispatcher : IDisposable
{
    private sealed class Operation
    {
        public CancellationTokenSource Source { get; } = new();
        public Task Cancellation { get; set; } = Task.CompletedTask;
        public bool Completing { get; set; }
    }

    private readonly object sync = new();
    private readonly IPluginCommandSession session;
    private readonly bool parallel;
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly Dictionary<string, Operation> operations = new(StringComparer.Ordinal);
    private bool closed;
    private long sequence;
    public event EventHandler<string>? MessageReceived;
    public event EventHandler? Drained;
    public bool HasPending { get { lock (sync) return operations.Count != 0; } }

    public PluginCommandDispatcher(IPluginCommandSession session)
    {
        this.session = session;
        parallel = session.CommandConcurrency == PluginCommandConcurrency.Parallel;
        session.EventReceived += OnEvent;
    }

    public Task<string> ExecuteAsync(string id, string command, JsonElement arguments)
    {
        lock (sync)
        {
            if (closed) throw new InvalidOperationException("Tool is closed.");
            if (operations.ContainsKey(id)) throw new ArgumentException("Command id is already active.");
            if (operations.Count >= 128) throw new InvalidOperationException("Too many pending commands (maximum 128).");
            var operation = new Operation();
            operations.Add(id, operation);
            // Clone before leaving the request document's lifetime; execute plugin code off the UI thread.
            var snapshot = arguments.Clone();
            // Reserve the sequential turn at admission, before thread-pool scheduling can reorder requests.
            var turn = parallel ? null : serial.WaitAsync(operation.Source.Token);
            return Task.Run(() => RunAsync(id, command, snapshot, operation, turn));
        }
    }

    public bool Cancel(string targetId)
    {
        lock (sync)
        {
            if (!operations.TryGetValue(targetId, out var operation) || operation.Completing) return false;
            if (!operation.Source.IsCancellationRequested) operation.Cancellation = operation.Source.CancelAsync();
            return true;
        }
    }

    private async Task<string> RunAsync(string id, string command, JsonElement arguments, Operation operation, Task? turn)
    {
        var entered = false;
        try
        {
            var token = operation.Source.Token;
            if (turn is not null) { await turn.ConfigureAwait(false); entered = true; }
            token.ThrowIfCancellationRequested();
            var result = await session.ExecuteCommandAsync(command, arguments, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return JsonSerializer.Serialize(new { type = "commandResult", id, success = true, result, status = "Completed" });
        }
        catch (OperationCanceledException) when (operation.Source.IsCancellationRequested)
        {
            return Failure("Cancelled");
        }
        catch (Exception ex) { return Failure(ex.Message); }
        finally
        {
            Task cancellation;
            lock (sync)
            {
                operation.Completing = true;
                cancellation = operation.Cancellation;
            }
            // Cancellation callbacks must finish before their token source and session are disposed.
            try { await cancellation.ConfigureAwait(false); } catch { /* Plugin cancellation callbacks may throw. */ }
            operation.Source.Dispose();
            if (entered) serial.Release();
            bool drained;
            lock (sync)
            {
                operations.Remove(id);
                drained = operations.Count == 0;
            }
            if (drained) Drained?.Invoke(this, EventArgs.Empty);
        }

        string Failure(string status) => JsonSerializer.Serialize(new { type = "commandResult", id, success = false, result = (object?)null, status });
    }

    private void OnEvent(object? sender, PluginToolEvent message)
    {
        lock (sync)
        {
            if (closed || string.IsNullOrWhiteSpace(message.Name) || message.Name.Length > 128) return;
            try
            {
                var json = JsonSerializer.Serialize(new { type = "event", name = message.Name, data = message.Data, sequence = ++sequence });
                if (json.Length <= 1_048_576) MessageReceived?.Invoke(this, json);
            }
            catch (Exception) { /* A malformed plugin event must not terminate the server's worker. */ }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (closed) return;
            closed = true;
            session.EventReceived -= OnEvent;
            MessageReceived = null;
            foreach (var operation in operations.Values)
                if (!operation.Completing && !operation.Source.IsCancellationRequested)
                    operation.Cancellation = operation.Source.CancelAsync();
        }
    }
}
