using System.Text.Json;
using Microsoft.Extensions.Logging;
using NonStopToolkit.Infrastructure.Logging;

namespace NonStopToolkit.Application.Tests;

public sealed class LocalFileLoggerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "nonstop-logging-tests-" + Guid.NewGuid().ToString("N"));
    private const string Secret = "PRIVATE-RECORD-4111111111111111-C:\\private\\input.log";
    private static Exception CaptureFailure()
    {
        try { throw new InvalidOperationException(Secret, new FormatException(Secret)); }
        catch (Exception error) { error.Data["input"] = Secret; return error; }
    }

    [Fact]
    public void RecordsStacksInnerErrorsAndContextWithoutMessagesStateOrPaths()
    {
        using var provider = new LocalFileLoggerProvider(directory);
        provider.CreateLogger("Tests.Parser").LogError(new EventId(201), CaptureFailure(), "Sensitive input: {Input}", Secret);
        var text = File.ReadAllText(Path.Combine(directory, "application.log"));
        Assert.DoesNotContain(Secret, text);
        Assert.DoesNotContain("private", text);
        Assert.DoesNotContain("LocalFileLoggerTests.cs", text);
        Assert.DoesNotContain("Sensitive input", text);
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        Assert.Equal(201, root.GetProperty("EventId").GetInt32());
        Assert.Equal("Parser workspace failed", root.GetProperty("Operation").GetString());
        Assert.Equal("Tests.Parser", root.GetProperty("Category").GetString());
        Assert.Equal(Environment.ProcessId, root.GetProperty("ProcessId").GetInt32());
        Assert.NotEmpty(root.GetProperty("RuntimeVersion").GetString()!);
        var exceptions = root.GetProperty("Exceptions");
        Assert.Equal(2, exceptions.GetArrayLength());
        Assert.Contains("InvalidOperationException", exceptions[0].GetProperty("Type").GetString());
        Assert.Contains("FormatException", exceptions[1].GetProperty("Type").GetString());
        Assert.Equal(0, exceptions[1].GetProperty("Parent").GetInt32());
        Assert.StartsWith("0x", exceptions[0].GetProperty("HResult").GetString());
        Assert.Contains(exceptions[0].GetProperty("Frames").EnumerateArray(), frame => frame.GetString()!.Contains(nameof(CaptureFailure)));
    }

    [Fact]
    public void LoggingNeverInvokesFormatterAndCapsAggregateDetails()
    {
        using var provider = new LocalFileLoggerProvider(directory);
        var aggregate = new AggregateException(Enumerable.Range(0, 100).Select(_ => new Exception(Secret)));
        provider.CreateLogger("Tests").Log(LogLevel.Critical, new EventId(103), Secret, aggregate,
            (_, _) => throw new InvalidOperationException("Formatter must not run"));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "application.log")));
        Assert.Equal(16, json.RootElement.GetProperty("Exceptions").GetArrayLength());
        Assert.DoesNotContain(Secret, json.RootElement.GetRawText());
    }

    [Fact]
    public void RotatesOldLogAndKeepsWriting()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "application.log");
        File.WriteAllText(path, new string('x', 1048577));
        using var provider = new LocalFileLoggerProvider(directory);
        provider.CreateLogger("Tests").LogWarning(new EventId(101), "ignored");
        Assert.Equal(1048577, new FileInfo(path + ".previous").Length);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(101, json.RootElement.GetProperty("EventId").GetInt32());
    }

    [Fact]
    public void UnwritableLogTargetDoesNotPropagateFailure()
    {
        Directory.CreateDirectory(directory);
        var blocked = Path.Combine(directory, "not-a-directory");
        File.WriteAllText(blocked, "test");
        using var provider = new LocalFileLoggerProvider(blocked);
        var error = Record.Exception(() => provider.CreateLogger("Tests").LogCritical(new EventId(105), CaptureFailure(), "ignored"));
        Assert.Null(error);
    }

    [Fact]
    public async Task ConcurrentEntriesRemainSeparateValidJsonLines()
    {
        using var provider = new LocalFileLoggerProvider(directory);
        var logger = provider.CreateLogger("Tests");
        await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => logger.LogError(new EventId(100), "ignored"))));
        var lines = File.ReadAllLines(Path.Combine(directory, "application.log"));
        Assert.Equal(24, lines.Length);
        foreach (var line in lines) { using var json = JsonDocument.Parse(line); Assert.Equal(100, json.RootElement.GetProperty("EventId").GetInt32()); }
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
