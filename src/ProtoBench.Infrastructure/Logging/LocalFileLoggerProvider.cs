using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ProtoBench.Infrastructure.Logging;

/// <summary>Bounded metadata only: never format log state or persist exception messages,
/// data dictionaries, arguments, source file names, input values or file paths.</summary>
public sealed class LocalFileLoggerProvider(string directory) : ILoggerProvider
{
    private readonly string directory = directory;
    private readonly object gate = new();
    public ILogger CreateLogger(string categoryName) => new LocalLogger(this, categoryName);
    public void Dispose() { }

    private sealed record ExceptionDetails(int? Parent, string Type, string HResult, string[] Frames);
    private static IReadOnlyList<ExceptionDetails> Describe(Exception? exception)
    {
        var results = new List<ExceptionDetails>();
        if (exception is null) return results;
        var pending = new Queue<(Exception Error, int? Parent)>();
        var seen = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        pending.Enqueue((exception, null));
        while (pending.Count > 0 && results.Count < 16)
        {
            var (error, parent) = pending.Dequeue();
            if (!seen.Add(error)) continue;
            var index = results.Count;
            var frames = (new StackTrace(error, false).GetFrames() ?? []).Take(48).Select(frame =>
            {
                var method = frame.GetMethod();
                return $"{method?.DeclaringType?.FullName}.{method?.Name} [IL {frame.GetILOffset()}]";
            }).Select(Limit).ToArray();
            results.Add(new(parent, Limit(error.GetType().FullName ?? error.GetType().Name), $"0x{error.HResult:X8}", frames));
            if (error is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions.Take(16)) pending.Enqueue((inner, index));
            }
            else if (error.InnerException is { } inner) pending.Enqueue((inner, index));
        }
        return results;
    }
    private static string Limit(string value) => value.Length <= 256 ? value : value[..256];
    private static string Operation(int id) => id switch
    {
        100 => "Desktop action failed", 101 => "Definition validation failed", 102 => "Unhandled UI exception",
        103 => "Unhandled managed exception", 104 => "Unobserved task exception", 105 => "Application startup failed",
        106 => "Process bootstrap failed", 201 => "Parser workspace failed", 202 => "Parser table failed", 301 => "Generator workspace failed",
        _ => "Application event"
    };
    private sealed class LocalLogger(LocalFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && logLevel != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            try
            {
                var entry = JsonSerializer.Serialize(new
                {
                    Timestamp = DateTimeOffset.UtcNow, Severity = level.ToString(), EventId = eventId.Id,
                    Operation = Operation(eventId.Id), Category = Limit(category),
                    ProcessId = Environment.ProcessId, ThreadId = Environment.CurrentManagedThreadId,
                    AppVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
                    RuntimeVersion = Environment.Version.ToString(), Exceptions = Describe(exception)
                });
                lock (owner.gate)
                {
                    Directory.CreateDirectory(owner.directory);
                    var path = Path.Combine(owner.directory, "application.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1048576)
                        File.Move(path, path + ".previous", true);
                    File.AppendAllText(path, entry + Environment.NewLine);
                }
            }
            catch (Exception) { /* Logging must never replace or propagate the original failure. */ }
        }
    }
}
