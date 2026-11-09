using Microsoft.Extensions.Logging.Abstractions;
using NonStopToolkit.Core.Documents;
using NonStopToolkit.Desktop.Features.Parsers;
using NonStopToolkit.Desktop.Services;
using NonStopToolkit.Infrastructure.Logs;
using NonStopToolkit.Parsing.Abstractions;
using NonStopToolkit.Parsing.Definitions;
using NonStopToolkit.Parsing.Engines;
using NonStopToolkit.Parsing.Registry;

namespace NonStopToolkit.Application.Tests;

public sealed class ParserRelatedTests
{
    private static RecordMessageParser Engine(int window = 300)
    {
        var profile = MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "definitions/messages/synthetic-record-v1.json")));
        return new(profile with { CorrelationWindowSeconds = window });
    }

    private static string Message(int id, string mti, string direction, string time = "14:14:00.00",
        string trace = "123456", string terminal = "DEMO", string connection = "LINK") =>
        $"TEST RECORD {id} FROM 17Jan {time} {direction} ON {connection} SEQ: {id:D5}\n Type={mti} Trace={trace} Terminal={terminal}\n";

    // Deliberately not a RecordMessageParser: the host must use the emitted record's rules.
    private sealed class StreamingWrapper(RecordMessageParser engine) : IStreamingLogParser
    {
        public string FormatId => "test.streaming-wrapper";
        public string Name => "Wrapped message parser";
        public int DetectionPriority => 100;
        public bool CanParse(ReadOnlySpan<byte> sample) => engine.CanParse(sample);
        public Task<ParseResult> ParseAsync(Stream input, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Large-log import must use the streaming API.");
        public Task ParseToAsync(Stream input, ILogParseSink sink, CancellationToken cancellationToken = default) =>
            engine.ParseToAsync(input, sink, cancellationToken);
    }

    private sealed class Settings : ISettingsStore
    {
        public Task<LocalSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new LocalSettings());
        public Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static MessageProfile ConfigurableProfile(string[] keys, bool sameConnection = false)
    {
        var profile = MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "definitions/messages/synthetic-record-v1.json")));
        var messages = new Dictionary<string, MessageTypeDefinition>(profile.Messages)
        {
            ["0800"] = new("Synthetic network request", "request", ["0810"], []),
            ["0810"] = new("Synthetic network reply", "reply", [], [])
        };
        profile = profile with { Fields = [.. profile.Fields, new("7", "Transmission time", ["Transmission"], "digits", 10)],
            Messages = messages, CorrelationKeys = keys, CorrelationSameConnection = sameConnection,
            CorrelationWindowSeconds = 10 };
        // Exercise the actual JSON profile boundary used by extensions.
        return MessageProfile.FromJson(System.Text.Json.JsonSerializer.Serialize(profile));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("Transmission")]
    [InlineData("Transmission time")]
    public async Task ConfiguredKeysMatchWithoutTerminalAcrossLinksAndAgreeWithInMemoryLookup(string transmissionKey)
    {
        var profile = ConfigurableProfile(["11", transmissionKey]);
        var engine = new RecordMessageParser(profile);
        string Network(int id, string time = "14:14:01.30", string date = "0117141400", string direction = "OUT",
            string mti = "0810", string trace = "123456") => Message(id, mti, direction, time,
                trace: trace, terminal: "", connection: "LINK-B") + $" Transmission={date}\n";
        var log = Message(1, "0800", "IN", terminal: "", connection: "LINK-A") + " Transmission=0117141400\n"
            + Network(2) + Network(3, date: "0117141401") + Network(4, time: "14:14:11.00")
            + Network(5, direction: "IN") + Network(6, mti: "0210") + Network(7, trace: "999999")
            + Network(8, date: "") + Network(9) + Network(10, date: "WRONG Transmission=0117141400");
        var all = (await engine.ParseAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(log)))).Records;
        var selected = all[0];
        Assert.Empty(MessageAnalysis.Value(selected, "41"));
        Assert.Equal(new[] { "11", transmissionKey }, selected.Correlation!.Keys);
        Assert.False(selected.Correlation.SameConnection);
        var expected = new long[] { 2, 9 };
        Assert.Equal(expected.Select(i => i.ToString()), MessageAnalysis.Related(selected, all, selected.Correlation)
            .Select(r => r.Message!.RecordNumber));
        Assert.Equal(expected.Select(i => i.ToString()), engine.Related(selected, all).Select(r => r.Message!.RecordNumber));
        Assert.Equal(expected.Select(i => i.ToString()), MessageAnalysis.Related(selected, all, new MessageProfileLookup(profile))
            .Select(r => r.Message!.RecordNumber));
        using var workspace = new ParserWorkspace(new ParserRegistry([new StreamingWrapper(engine)]),
            new SessionSettings(new Settings()), NullLogger<ParserWorkspace>.Instance);
        await workspace.ParseTextAsync(log);
        Assert.Equal("Completed", workspace.Status);
        await workspace.LoadPageAsync(new(Direction: "IN", PageSize: 1));
        var stored = await workspace.GetRecordAsync(1);
        Assert.Null(workspace.RelatedUnavailableReason(stored));
        Assert.Equal(expected, (await workspace.GetRelatedAsync(1, stored)).Select(r => r.Id));
        Assert.Equal(1, Assert.Single(await workspace.GetRelatedAsync(2, await workspace.GetRecordAsync(2))).Id);
        Assert.Empty(MessageAnalysis.Related(selected, all, selected.Correlation with { SameConnection = true }));
        Assert.Empty(await workspace.GetRelatedAsync(1, stored with { Correlation = stored.Correlation! with { SameConnection = true } }));
        var missing = await workspace.GetRecordAsync(8);
        Assert.Contains(transmissionKey, workspace.RelatedUnavailableReason(missing));
        Assert.Empty(await workspace.GetRelatedAsync(8, missing));
    }

    [Fact]
    public async Task CustomKeysDoNotImplicitlyRequireStanOrTerminalAndRemainScopedToProfile()
    {
        var engine = new RecordMessageParser(ConfigurableProfile(["7"]));
        var log = Message(1, "0800", "IN", trace: "", terminal: "", connection: "A") + " Transmission=0117141400\n"
            + Message(2, "0810", "OUT", "14:14:01.00", trace: "", terminal: "", connection: "B") + " Transmission=0117141400\n";
        using var store = new LogStore();
        await store.ImportAsync(new StreamingWrapper(engine), new MemoryStream(System.Text.Encoding.UTF8.GetBytes(log)), default);
        var selected = await store.ReadAsync(1);
        Assert.Equal(2, Assert.Single(await store.RelatedAsync(1, selected)).Id);
        var all = (await engine.ParseAsync(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(log)))).Records;
        Assert.Single(MessageAnalysis.Related(all[0], all, all[0].Correlation!));
        Assert.Empty(await store.RelatedAsync(1, selected with { Message = selected.Message! with { ProfileId = "other" } }));
        Assert.Empty(MessageAnalysis.Related(all[0], [all[1] with { Message = all[1].Message! with { ProfileId = "other" } }], all[0].Correlation!));
    }

    [Fact]
    public async Task StreamingExtensionFindsRelatedAcrossPagesAndFiltersInBothDirections()
    {
        var wrapper = new StreamingWrapper(Engine());
        using var workspace = new ParserWorkspace(new ParserRegistry([wrapper]), new SessionSettings(new Settings()),
            NullLogger<ParserWorkspace>.Instance);
        var log = Message(1, "0200", "IN")
            + string.Concat(Enumerable.Range(2, 2000).Select(i => Message(i, "0200", "IN", trace: i.ToString("D6"))))
            + Message(2002, "0210", "OUT", "14:14:01.00");
        await workspace.ParseTextAsync(log);
        Assert.Equal("Completed", workspace.Status);
        Assert.Equal(2000, workspace.Page!.Rows.Count);
        await workspace.LoadPageAsync(new(Direction: "IN", PageSize: 1));
        var request = await workspace.GetRecordAsync(1);
        Assert.Equal(300, request.Correlation!.WindowSeconds);
        Assert.Contains("0210", request.Correlation.MessageTypes);
        Assert.Null(workspace.RelatedUnavailableReason(request));
        Assert.Contains("did not provide related-message rules", workspace.RelatedUnavailableReason(request with { Correlation = null }));
        Assert.Contains("known message date and time", workspace.RelatedUnavailableReason(request with
            { Message = request.Message! with { LoggedDateTime = "unknown" } }));
        var reply = Assert.Single(await workspace.GetRelatedAsync(1, request));
        Assert.Equal(2002, reply.Id);
        var replyRecord = await workspace.GetRecordAsync(reply.Id);
        Assert.Contains("0200", replyRecord.Correlation!.MessageTypes);
        Assert.Equal(1, Assert.Single(await workspace.GetRelatedAsync(reply.Id, replyRecord)).Id);
        Assert.Equal(1, workspace.Page.Rows[0].Id); // Lookup never changes the active filtered page.
    }

    [Fact]
    public async Task StoredCorrelationPreservesCustomWindowAndRejectsUnrelatedCandidates()
    {
        using var store = new LogStore();
        var log = Message(1, "0200", "IN") + Message(2, "0210", "OUT", "14:14:05.00")
            + Message(3, "0210", "OUT", "14:14:11.00") // Outside the custom window.
            + Message(4, "0810", "OUT", "14:14:05.00") // Unrelated MTI.
            + Message(5, "0210", "IN", "14:14:05.00") // Same direction.
            + Message(6, "0210", "OUT", "14:14:05.00", terminal: "OTHER")
            + Message(7, "0210", "OUT", "14:14:05.00", connection: "OTHER")
            + Message(8, "0210", "OUT", "14:14:05.00", trace: "999999")
            + Message(9, "0210", "OUT", "14:14:06.00"); // Ambiguous replies both remain visible.
        await store.ImportAsync(new StreamingWrapper(Engine(10)), new MemoryStream(System.Text.Encoding.UTF8.GetBytes(log)), default);
        var request = await store.ReadAsync(1);
        Assert.Equal(10, request.Correlation!.WindowSeconds);
        Assert.Equal(new long[] { 2, 9 }, (await store.RelatedAsync(1, request)).Select(r => r.Id));
        Assert.Equal(1, Assert.Single(await store.RelatedAsync(2, await store.ReadAsync(2))).Id);
        Assert.Empty(await store.RelatedAsync(1, request with { Correlation = null }));
    }
}
