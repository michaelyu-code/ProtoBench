using System.Diagnostics;
using System.Text;
using ProtoBench.Core.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoBench.Desktop.Features.Parsers;
using ProtoBench.Desktop.Services;
using ProtoBench.Infrastructure.Logs;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;
using ProtoBench.Parsing.Registry;
using Xunit.Abstractions;

namespace ProtoBench.Application.Tests;

public sealed class LargeLogFactAttribute : FactAttribute
{
    public LargeLogFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("PROTOBENCH_LARGE_LOG_TEST") != "1")
            Skip = "Set PROTOBENCH_LARGE_LOG_TEST=1 to run the >2 GiB disk/memory integration test.";
    }
}

public sealed class LargeLogTests(ITestOutputHelper output)
{
    private sealed class Settings : ISettingsStore
    {
        public Task<LocalSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LocalSettings());
        public Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [LargeLogFact]
    public async Task FullTwoGiBFileIndexesToLastRecordWithBoundedMemory()
    {
        var parser = new JsonDefinedParser(new ParserDefinition
        {
            Id = "large", Name = "Large", DetectionPrefix = "SYN|",
            Fields = [new() { Name = "kind" }, new() { Name = "value" }, new() { Name = "number" }]
        });
        var line = Encoding.UTF8.GetBytes("SYN|" + new string('x', 32768) + "|1\r\n");
        long repetitions = (2L * 1024 * 1024 * 1024) / line.Length + 1;
        var path = Path.Combine(Path.GetTempPath(), "protobench-large-test-" + Guid.NewGuid().ToString("N") + ".log");
        var timer = Stopwatch.StartNew();
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                for (long i = 0; i < repetitions; i++) await file.WriteAsync(line);
                await file.WriteAsync("SYN|unique end marker|2"u8.ToArray());
            }
            var bytes = new FileInfo(path).Length;
            Assert.True(bytes > int.MaxValue);
            output.WriteLine($"Source: {bytes:N0} bytes; {repetitions + 1:N0} records; creation {timer.Elapsed}.");
            using var workspace = new ParserWorkspace(new ParserRegistry([parser]), new SessionSettings(new Settings()), NullLogger<ParserWorkspace>.Instance);
            timer.Restart();
            await workspace.OpenAsync(path);
            output.WriteLine($"Import: {timer.Elapsed}; peak process working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576d:N1} MiB.");
            Assert.Equal("Completed", workspace.Status);
            Assert.Equal(repetitions + 1, workspace.Page!.Total);
            Assert.Equal(2000, workspace.Page.Rows.Count);
            Assert.False(workspace.IsTruncated);
            timer.Restart();
            await workspace.LoadPageAsync(new LogQuery(Search: "unique end marker"));
            Assert.Equal(1, workspace.Page!.Matches);
            var final = Assert.Single(workspace.Page.Rows);
            Assert.Equal(repetitions + 1, final.LineNumber);
            Assert.Equal("unique end marker", (await workspace.GetRecordAsync(final.Id)).Fields[1].Value);
            output.WriteLine($"Full-log end-marker search: {timer.Elapsed}; peak: {Process.GetCurrentProcess().PeakWorkingSet64 / 1048576d:N1} MiB.");
            Assert.True(Process.GetCurrentProcess().PeakWorkingSet64 < 768L * 1024 * 1024, "The isolated large-file test exceeded its 768 MiB working-set budget.");
        }
        finally { File.Delete(path); }
    }
}


