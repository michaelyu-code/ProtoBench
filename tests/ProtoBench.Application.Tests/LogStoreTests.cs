using System.Text;
using ProtoBench.Core.Documents;
using ProtoBench.Infrastructure.Logs;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Application.Tests;

public sealed class LogStoreTests
{
    [Fact]
    public async Task ExtensionRawTextRetainsMixedLineEndingsBlankLinesAndTrailingNewline()
    {
        const string original = "header\r\n\r\nfield=demo\nnext\rlast\r\n";
        using var store = new LogStore();
        await store.ImportAsync(new OriginalTextPlugin(original), new MemoryStream("demo"u8.ToArray()), default);
        var row = Assert.Single((await store.QueryAsync(new())).Rows);
        Assert.Equal(original, (await store.ReadAsync(row.Id)).RawText);
    }

    private sealed class OriginalTextPlugin(string original) : ILogParser
    {
        public string FormatId => "test.original";
        public string Name => "Original text regression";
        public int DetectionPriority => 1;
        public bool CanParse(ReadOnlySpan<byte> sample) => true;
        public Task<ParseResult> ParseAsync(Stream input, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ParseResult([new ParsedRecord(1, original, [])], []));
    }

    private static JsonDefinedParser Lines() => new(new ParserDefinition
    {
        Id = "lines", Name = "Lines", Fields = [new() { Name = "kind" }, new() { Name = "value" }, new() { Name = "number" }]
    });

    [Fact]
    public async Task DiskPagesDefaultTo2000AndSearchFindsMatchesAfterOldRecordLimit()
    {
        using var store = new LogStore();
        var text = string.Concat(Enumerable.Range(1, 10002).Select(i => $"SYN|value{i}|{i}\n")) + "SYN|café END%_|last";
        await store.ImportAsync(Lines(), new MemoryStream(Encoding.UTF8.GetBytes(text)), default);
        var first = await store.QueryAsync(new());
        Assert.Equal(2000, first.Rows.Count);
        Assert.Equal(10003, first.Total);
        var second = await store.QueryAsync(new(PageIndex: 1));
        Assert.Equal(2001, second.Rows[0].LineNumber);
        var found = await store.QueryAsync(new(Search: "CAFÉ END%_", Field: "VaLuE"));
        var row = Assert.Single(found.Rows);
        Assert.Equal(10003, row.LineNumber);
        Assert.Equal("café END%_", (await store.ReadAsync(row.Id)).Fields[1].Value);
        Assert.Empty((await store.QueryAsync(new(Search: "END", Field: "number"))).Rows);
        Assert.Equal(10003, (await store.QueryAsync(new(Descending: true))).Rows[0].LineNumber);
        Assert.Equal(5, (await store.QueryAsync(new(PageIndex: long.MaxValue / 1000))).PageIndex);
    }

    [Fact]
    public async Task FiltersAndSortAreGlobalAndDecodedContentRoundTrips()
    {
        using var store = new LogStore();
        var parser = new RecordMessageParser(MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json"))));
        const string request = "TEST RECORD 1 FROM 17Jan 14:14:58.83 IN ON DEMO SEQ: 00001\n Type=0200 Trace=123456 Terminal=TEST\n";
        const string reply = "TEST RECORD 2 FROM 17Jan 14:14:58.84 OUT ON DEMO SEQ: 00002\n Type=0210 Trace=123456 Terminal=TEST Unknown=x\n";
        await store.ImportAsync(parser, new MemoryStream(Encoding.UTF8.GetBytes(request + reply)), default);
        var filtered = await store.QueryAsync(new(Direction: "OUT", Role: "reply", Sort: "11"));
        Assert.Equal(1, filtered.Matches);
        var selected = await store.ReadAsync(Assert.Single(filtered.Rows).Id);
        Assert.Equal(reply, selected.RawText);
        Assert.NotEmpty(selected.Findings);
        var related = await store.RelatedAsync(2, selected, parser);
        Assert.Equal(1, Assert.Single(related).Id);
    }

    [Fact]
    public async Task DisposingDeletesIndexAndCancelledQueriesDoNotCorruptIt()
    {
        var store = new LogStore();
        var path = store.DatabasePath;
        try
        {
            await store.ImportAsync(Lines(), new MemoryStream("SYN|one|1\nSYN|two|2"u8.ToArray()), default);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.QueryAsync(new(Search: "one"), cancellation.Token));
            Assert.Equal(2, (await store.QueryAsync(new())).Total);
            Assert.True(File.Exists(path));
        }
        finally { store.Dispose(); }
        Assert.False(File.Exists(path));
        store.Dispose();
    }

    private sealed class Legacy : ILogParser
    {
        public string FormatId => "legacy";
        public string Name => "Legacy";
        public int DetectionPriority => 1;
        public bool CanParse(ReadOnlySpan<byte> sample) => true;
        public Task<ParseResult> ParseAsync(Stream input, CancellationToken cancellationToken = default) => Task.FromResult(new ParseResult([], []));
    }

    [Fact]
    public async Task LegacyPluginsExplainWhyTheyCannotAcceptLargeInput()
    {
        using var store = new LogStore();
        using var input = new MemoryStream(new byte[16 * 1024 * 1024 + 1]);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(new Legacy(), input, default));
        Assert.Contains("IStreamingLogParser", error.Message);
    }

    private sealed class CancelOnProgress(CancellationTokenSource cancellation) : IProgress<LogImportProgress>
    {
        public void Report(LogImportProgress value) => cancellation.Cancel();
    }

    [Fact]
    public async Task ActiveImportCanBeCancelledAndItsPartialIndexIsDeleted()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new LogStore(new CancelOnProgress(cancellation));
        var path = store.DatabasePath;
        try
        {
            var text = string.Concat(Enumerable.Repeat("SYN|" + new string('x', 2048) + "|1\n", 3000));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ImportAsync(Lines(),
                new MemoryStream(Encoding.UTF8.GetBytes(text)), cancellation.Token));
            Assert.InRange(store.Count, 1, 2999);
        }
        finally { store.Dispose(); }
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".lock"));
    }

    [Fact]
    public async Task ManyMalformedRecordsHaveBoundedDiagnosticsAndDoNotHideValidTail()
    {
        using var store = new LogStore();
        var text = string.Concat(Enumerable.Repeat("bad\n", 2001)) + "SYN|tail|1";
        await store.ImportAsync(Lines(), new MemoryStream(Encoding.UTF8.GetBytes(text)), default);
        Assert.Equal(1001, store.Diagnostics.Count);
        Assert.Contains("2,001", store.Diagnostics[^1].Message);
        Assert.Equal(2002, Assert.Single((await store.QueryAsync(new())).Rows).LineNumber);
    }
}


