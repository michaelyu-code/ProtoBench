using System.Text;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Infrastructure.Logs;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Application.Tests;

public sealed class LogTableTests
{
    private static RecordMessageParser Parser() => new(MessageProfile.FromJson(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json"))));
    private static string Message(int number, string mti, string extra = "") =>
        $"TEST RECORD {number} FROM 17Jan 14:14:58.83 {(mti == "0200" ? "IN" : "OUT")} ON DEMO SEQ: {number:D5}\r\n" +
        $" Type={mti} Trace={100000 + number} Terminal=DEMO {extra}\r\n";

    [Fact]
    public async Task ProjectionUsesExistingFilteredSortedPageAndCatalogIncludesOtherPages()
    {
        using var store = new LogStore();
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(Message(1, "0200") + Message(2, "0210") +
            Message(3, "0200", "Future-field=later") + Message(4, "0210")));
        await store.ImportAsync(Parser(), input, default);
        var page = await store.QueryAsync(new(Role: "reply", Sort: "11", Descending: true, PageSize: 1));
        var table = await store.ReadTableAsync(page.Rows, ["field:11", "meta:record", "meta:role", "field:FUTURE-FIELD"]);
        Assert.Equal(2, page.Matches);
        Assert.Equal(new[] { "field:11", "meta:record", "meta:role", "field:FUTURE-FIELD" }, table.Columns.Select(c => c.Key));
        var row = Assert.Single(table.Rows);
        Assert.Equal(page.Rows[0].Id, row.Id);
        Assert.Equal("100004", row.Values["field:11"]);
        Assert.Equal("4", row.Values["meta:record"]);
        Assert.Equal("reply", row.Values["meta:role"]);
        Assert.False(row.Values.ContainsKey("field:FUTURE-FIELD"));
        Assert.Contains(store.TableColumns, c => c.Key == "field:FUTURE-FIELD");
        Assert.False(table.ValuesShortened);
    }

    [Fact]
    public async Task RepeatedFieldsArePreservedAndLongCellsDoNotChangeOriginalPayload()
    {
        using var store = new LogStore();
        var text = Message(1, "0200", "Trace=654321 Padded=" + new string('x', 6000));
        await store.ImportAsync(Parser(), new MemoryStream(Encoding.UTF8.GetBytes(text)), default);
        var page = await store.QueryAsync(new());
        var table = await store.ReadTableAsync(page.Rows, ["field:11", "field:60"]);
        var row = Assert.Single(table.Rows);
        Assert.Equal("100001\n654321", row.Values["field:11"]);
        Assert.True(table.ValuesShortened);
        Assert.Contains("preview shortened", row.Values["field:60"]);
        Assert.True(row.Values["field:60"].Length < 4200);
        var record = await store.ReadAsync(row.Id);
        Assert.Equal(6000, record.Fields.Single(f => f.DataElement == "60").Value.Length);
        Assert.Equal(text, record.RawText);
    }

    [Fact]
    public async Task TableTextBudgetIsBoundedAcrossLargePages()
    {
        using var store = new LogStore();
        var value = new string('x', 4096);
        var source = string.Concat(Enumerable.Range(1, 400).Select(n => Message(n, "0200", $"Acceptor={value} Padded={value} Context={value}")));
        await store.ImportAsync(Parser(), new MemoryStream(Encoding.UTF8.GetBytes(source)), default);
        var page = await store.QueryAsync(new());
        var table = await store.ReadTableAsync(page.Rows, ["field:42", "field:60", "field:61"]);
        Assert.Equal(400, table.Rows.Count);
        Assert.True(table.ValuesShortened);
        Assert.True(table.Rows.Sum(r => r.Values.Values.Sum(v => v.Length)) <= LogTable.MaximumPageCharacters + 1200 * 80);
    }

    [Fact]
    public async Task ProjectionHonorsCancellationAndRejectsUnknownColumns()
    {
        using var store = new LogStore();
        await store.ImportAsync(Parser(), new MemoryStream(Encoding.UTF8.GetBytes(Message(1, "0200"))), default);
        var page = await store.QueryAsync(new());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadTableAsync(page.Rows, ["field:missing"]));
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadTableAsync(page.Rows, ["field:11"], canceled.Token));
    }

    [Fact]
    public async Task DefaultTwoThousandMessagePageProjectsWithoutLoadingDecodedPayloads()
    {
        using var store = new LogStore();
        var input = string.Concat(Enumerable.Range(1, 2001).Select(n => Message(n, "0200")));
        await store.ImportAsync(Parser(), new MemoryStream(Encoding.UTF8.GetBytes(input)), default);
        var page = await store.QueryAsync(new());
        var table = await store.ReadTableAsync(page.Rows, ["meta:record", "field:11"]);
        Assert.Equal(2000, table.Rows.Count);
        Assert.Equal("1", table.Rows[0].Values["meta:record"]);
        Assert.Equal("2000", table.Rows[^1].Values["meta:record"]);
        var next = await store.QueryAsync(new(PageIndex: 1));
        Assert.Equal("2001", Assert.Single((await store.ReadTableAsync(next.Rows, ["meta:record"])).Rows).Values["meta:record"]);
    }

    [Fact]
    public async Task FieldCheckProjectionMatchesTrafficLightsIncludingAbsentMandatoryFields()
    {
        using var store = new LogStore();
        var input = Message(1, "0200") + Message(2, "0200", "Trace=654321").Replace("Trace=100002", "Trace=oops")
            + Message(3, "0200", "Trace=654321") + Message(4, "0200").Replace("Trace=100004", "");
        await store.ImportAsync(Parser(), new MemoryStream(Encoding.UTF8.GetBytes(input)), default);
        var page = await store.QueryAsync(new());
        var rows = (await store.ReadTableAsync(page.Rows, ["field:11"])).Rows;
        Assert.False(rows[0].Checks.ContainsKey("field:11"));
        Assert.Equal(2, rows[1].Checks["field:11"]);
        Assert.Equal(1, rows[2].Checks["field:11"]);
        Assert.Equal(2, rows[3].Checks["field:11"]);
        Assert.False(rows[3].Values.ContainsKey("field:11"));
    }

    [Fact]
    public async Task MissingFieldColumnGetsItsProperNameWhenAValueAppearsLater()
    {
        using var store = new LogStore();
        var input = Message(1, "0200").Replace("Trace=100001", "") + Message(2, "0200");
        await store.ImportAsync(Parser(), new MemoryStream(Encoding.UTF8.GetBytes(input)), default);
        Assert.Contains(store.TableColumns, c => c.Key == "field:11" && c.Header.Contains("Trace") && !c.Header.Contains("Missing field"));
    }

    [Fact]
    public async Task PluginFindingsUsingTheFieldNameAreAppliedToItsElementColumn()
    {
        var profile = MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "definitions/messages/synthetic-record-v1.json")));
        profile = profile with { Fields = profile.Fields.Select(f => f.Id == "3" ? f with { Decoder = "test.named" } : f).ToArray() };
        using var store = new LogStore();
        await store.ImportAsync(new RecordMessageParser(profile, [new NamedFindingDecoder()]),
            new MemoryStream(Encoding.UTF8.GetBytes(Message(1, "0200", "Operation=123456"))), default);
        var page = await store.QueryAsync(new());
        var row = Assert.Single((await store.ReadTableAsync(page.Rows, ["field:3"])).Rows);
        Assert.Equal("123456", row.Values["field:3"]);
        Assert.Equal(1, row.Checks["field:3"]);
    }

    private sealed class NamedFindingDecoder : IFieldDecoder
    {
        public string Id => "test.named";
        public FieldDecodeResult Decode(FieldDecodeContext context, CancellationToken cancellationToken) =>
            new([], [new("Operation", "Review", "Synthetic plugin review", "test.named-rule")]);
    }
}
