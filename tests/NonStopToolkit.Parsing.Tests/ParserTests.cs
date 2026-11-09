using System.Text;
using NonStopToolkit.Parsing.Definitions;
using NonStopToolkit.Parsing.Engines;
using NonStopToolkit.Parsing.Registry;
namespace NonStopToolkit.Parsing.Tests;

public sealed class ParserTests
{
    private static ParserDefinition Definition(string name = "synthetic-delimited") => ParserDefinition.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions", "parsers", name + ".json")));
    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
    [Fact]
    public async Task ParsesDelimitedFixtureAndTracksUtf8Offsets()
    {
        var parser = new JsonDefinedParser(Definition());
        await using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "samples", "synthetic-delimited.txt"));
        var result = await parser.ParseAsync(input);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Records.Count);
        var field = result.Records[1].Fields[1];
        Assert.Equal("café", field.Value);
        Assert.Equal(7, field.Source.ByteLength);
        Assert.Equal(6, field.Source.CharacterLength);
        Assert.Equal(4, field.Source.CharacterOffset);
        input.Position = field.Source.ByteOffset;
        var bytes = new byte[field.Source.ByteLength];
        await input.ReadExactlyAsync(bytes);
        Assert.Equal(" café ", Encoding.UTF8.GetString(bytes));
    }
    [Fact]
    public async Task ParsesFixedWidthFixture()
    {
        var parser = new JsonDefinedParser(Definition("synthetic-fixed"));
        await using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "samples", "synthetic-fixed.txt"));
        var result = await parser.ParseAsync(input);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("ALPHA", result.Records[0].Fields[1].Value);
        Assert.Equal("0007", result.Records[1].Fields[2].Value);
    }
    [Fact]
    public void DetectionReturnsAllCandidatesInPriorityOrder()
    {
        var first = new JsonDefinedParser(Definition());
        var second = new JsonDefinedParser(Definition() with
        {
            Id = "other",
            DetectionPriority = 200
        });
        var registry = new ParserRegistry([first, second]);
        Assert.Equal(new[] { "other", first.FormatId }, registry.Detect("SYN|x|1"u8).Select(p => p.FormatId));
        Assert.Empty(registry.Detect("unknown"u8));
        Assert.Same(first, registry.Get(first.FormatId.ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => new ParserRegistry([first, first]));
    }
    [Theory]
    [InlineData("{bad json")]
    [InlineData("{\"schemaVersion\":99}")]
    [InlineData("{}")]
    [InlineData("{\"unknown\":true}")]
    public void RejectsInvalidJsonDefinitions(string json) => Assert.ThrowsAny<Exception>(() => ParserDefinition.FromJson(json));
    [Fact]
    public void RejectsInvalidRules()
    {
        var d = Definition();
        Assert.Throws<ArgumentException>(() => (d with { Strategy = "code" }).Validate());
        Assert.Throws<ArgumentException>(() => (d with { Encoding = "ebcdic" }).Validate());
        Assert.Throws<ArgumentException>(() => (d with { Encoding = null! }).Validate());
        Assert.Throws<ArgumentException>(() => (d with { Fields = [new() { Name = "x", Transform = "execute" }] }).Validate());
        Assert.Throws<ArgumentException>(() => (d with { Strategy = "fixed-width", Fields = [new() { Name = "x", Start = -1, Length = 2 }] }).Validate());
    }
    [Fact]
    public async Task RegexUsesNamedGroupsAndTransforms()
    {
        var d = Definition() with
        {
            Strategy = "regex",
            Pattern = "^(?<kind>SYN):(?<value>.+)$",
            Fields = [new() { Name = "kind" }, new() { Name = "value", Transform = "upper" }]
        };
        var result = await new JsonDefinedParser(d).ParseAsync(Input("SYN:hello"));
        Assert.Equal("HELLO", result.Records[0].Fields[1].Value);
    }
    [Fact]
    public async Task MalformedRecordDoesNotDiscardValidRecords()
    {
        var result = await new JsonDefinedParser(Definition()).ParseAsync(Input("bad\nSYN|ok|1"));
        Assert.Single(result.Records);
        Assert.Single(result.Diagnostics);
        Assert.Equal(1, result.Diagnostics[0].Line);
    }
    [Fact]
    public async Task ReportsInvalidEncodedBytes()
    {
        var result = await new JsonDefinedParser(Definition()).ParseAsync(new MemoryStream([0xFF]));
        Assert.Single(result.Diagnostics);
        Assert.Empty(result.Records);
    }
    [Fact]
    public async Task HandlesCrLfAndBomOffsets()
    {
        var result = await new JsonDefinedParser(Definition()).ParseAsync(Input("\uFEFFSYN|ok|1\r\nSYN|b|2"));
        Assert.Empty(result.Diagnostics);
        Assert.Equal(3, result.Records[0].Fields[0].Source.ByteOffset);
        Assert.Equal(13, result.Records[1].Fields[0].Source.ByteOffset);
    }
    [Fact]
    public async Task EnforcesPreviewAndLineBounds()
    {
        var parser = new JsonDefinedParser(Definition());
        Assert.True((await parser.ParseAsync(Input(new string('x', 65537)))).IsTruncated);
        var result = await parser.ParseAsync(Input(string.Concat(Enumerable.Repeat("SYN|x|1\n", 10001))));
        Assert.True(result.IsTruncated);
        Assert.Equal(10000, result.Records.Count);
    }
    [Fact]
    public async Task HonorsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new JsonDefinedParser(Definition()).ParseAsync(Input("SYN|x|1"), cts.Token));
    }
    [Fact]
    public async Task BoundsTotalPreviewData()
    {
        // Fewer than the record limit, but more than the total byte budget.
        var line = "SYN|" + new string('x', 32768) + "|1\n";
        var result = await new JsonDefinedParser(Definition()).ParseAsync(Input(string.Concat(Enumerable.Repeat(line, 520))));
        Assert.True(result.IsTruncated);
        Assert.Contains(result.Diagnostics, d => d.Code == "parse.byte-limit");
        Assert.True(result.Records.Count < 520);
    }
}
