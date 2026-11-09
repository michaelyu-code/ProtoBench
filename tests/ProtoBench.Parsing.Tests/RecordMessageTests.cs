using System.Text;
using ProtoBench.Core.Documents;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Parsing.Tests;

public sealed class RecordMessageTests
{
    [Fact]
    public void ExistingProfilesAndCorrelationPayloadsKeepTheirOriginalDefaults()
    {
        var profile = Profile();
        Assert.Equal(new[] { "11", "41" }, profile.CorrelationKeys);
        Assert.True(profile.CorrelationSameConnection);
        var correlation = System.Text.Json.JsonSerializer.Deserialize<MessageCorrelation>(
            """{"WindowSeconds":300,"MessageTypes":["0210"]}""")!;
        Assert.Equal(new[] { "11", "41" }, correlation.Keys);
        Assert.True(correlation.SameConnection);
        Assert.True(correlation.IsValid());
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[\"Trace\",\"trace\"]")]
    [InlineData("[\"11\",\" \" ]")]
    public void InvalidCorrelationKeysAreRejectedAtTheProfileBoundary(string keysJson)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(Profile()))!;
        json["CorrelationKeys"] = System.Text.Json.Nodes.JsonNode.Parse(keysJson);
        Assert.Throws<ArgumentException>(() => MessageProfile.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void CorrelationPolicyUsesAnImmutableSnapshotOfTheConfiguredKeys()
    {
        var profile = Profile() with { CorrelationKeys = ["11", "7"], CorrelationSameConnection = false };
        var parser = new RecordMessageParser(profile);
        profile.CorrelationKeys[1] = "41";
        var rule = parser.GetCorrelation("0200")!;
        Assert.Equal(new[] { "11", "7" }, rule.Keys);
        Assert.False(rule.SameConnection);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)rule.Keys)[1] = "41");
        Assert.False(new MessageCorrelation(300, ["0210"]) { Keys = [] }.IsValid());
        Assert.False(new MessageCorrelation(300, ["0210"]) { Keys = Enumerable.Range(0, 33).Select(i => i.ToString()).ToArray() }.IsValid());
    }

    private static MessageProfile Profile() => MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json")));
    private static Task<ParseResult> Parse(string text) => new RecordMessageParser(Profile()).ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)));
    private const string Request = "TEST RECORD 18223 FROM 17Jan 14:14:58.83 IN ON DEMO SEQ: 02794 \r\n" +
        " Type=0200 Operation=123456 Amount=000000000100 Trace=000346\r\n" +
        " Terminal=DEMO Acceptor=TEST-ACCEPTOR\r\n" +
        " ICC=9F02:06:000000003500/9F3\r\n 3:03:E0F8C8/95:05:0800000000/\r\n Context=000338\r\n\r\n";
    private const string Reply = "TEST RECORD 18224 FROM 17Jan 14:14:58.84 OUT ON DEMO SEQ: 02795\r\n" +
        " Type=0210 Operation=123456 Amount=000000000100 Trace=000346\r\n" +
        " Response=96 Terminal=DEMO Acceptor=TEST-ACCEPTOR Context=2";

    [Fact]
    public async Task VerboseChecksDescribeSuccessfulAndFailedRulesWithoutChangingFindings()
    {
        var parser = new RecordMessageParser(Profile());
        var record = Assert.Single((await parser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(Request)))).Records);
        var findings = record.Findings.ToArray();
        var checks = parser.GetCheckDetails(record);
        Assert.Contains(checks, c => c.Field == "11" && c.Check == "Exact length" && c.Outcome == "Passed");
        Assert.Contains(checks, c => c.Field == "11" && c.Check == "Character type" && c.Outcome == "Passed");
        Assert.Contains(checks, c => c.Field == "11" && c.Check == "Field presence" && c.Outcome == "Passed");
        Assert.Contains(checks, c => c.Field == "55" && c.Check == "Conditional presence" && c.Outcome == "Not evaluated");
        Assert.Equal(findings, record.Findings);
        var invalid = Assert.Single((await parser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(
            Request.Replace("Trace=000346", "Trace=oops").Replace("Operation=123456", "Operation=123456 Future=1 Operation=000000"))))).Records);
        var invalidChecks = parser.GetCheckDetails(invalid);
        Assert.Contains(invalidChecks, c => c.Field == "11" && c.Check == "Exact length" && c.Outcome == "Failed");
        Assert.Contains(invalidChecks, c => c.Field == "11" && c.Check == "Character type" && c.Outcome == "Failed");
        Assert.Contains(invalidChecks, c => c.Field == "3" && c.Check == "Duplicate detection" && c.Outcome == "Review");
        Assert.Contains(invalidChecks, c => c.Field == "Future" && c.Outcome == "Not evaluated");
        var missing = Assert.Single((await parser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(Request.Replace("Trace=000346", ""))))).Records);
        Assert.Contains(parser.GetCheckDetails(missing), c => c.Field == "11" && c.Check == "Field presence" && c.Outcome == "Failed");
    }

    [Fact]
    public async Task VerboseDecoderReportDoesNotClaimUnreportedPluginChecksPassed()
    {
        var profile = Profile();
        var index = Array.FindIndex(profile.Fields, f => f.Id == "3");
        profile.Fields[index] = profile.Fields[index] with { Decoder = "missing.decoder" };
        var parser = new RecordMessageParser(profile);
        var record = Assert.Single((await parser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(Request)))).Records);
        Assert.Contains(parser.GetCheckDetails(record), c => c.Field == "3" && c.Check == "Field decoder" && c.Outcome == "Not completed");
    }

    [Fact]
    public async Task SyntheticMessagesSupportEveryEditorNewlineAndPreserveSource()
    {
        var fixture = (Request + Reply).ReplaceLineEndings("\n");
        foreach (var newline in new[] { "\r", "\n", "\r\n" })
        {
            var text = fixture.Replace("\n", newline);
            var parser = new RecordMessageParser(Profile());
            Assert.True(parser.CanParse(Encoding.UTF8.GetBytes(text)));
            var result = await Parse(text);
            Assert.Empty(result.Diagnostics);
            Assert.Equal(new[] { "18223", "18224" }, result.Records.Select(r => r.Message!.RecordNumber));
            Assert.Equal(new[] { "0200", "0210" }, result.Records.Select(r => r.Message!.MessageType));
            Assert.Equal(text, string.Concat(result.Records.Select(r => r.RawText)));
            Assert.Equal(new long[] { 1, Request.Count(c => c == '\n') + 1 }, result.Records.Select(r => r.LineNumber));
            Assert.Equal(3, result.Records[0].Fields.Single(f => f.DataElement == "55").Children!.Count);
            var bytes = Encoding.UTF8.GetBytes(text);
            foreach (var record in result.Records)
            foreach (var field in record.Fields)
                Assert.Equal(record.RawText.Substring(field.Source.CharacterOffset, field.Source.CharacterLength),
                    Encoding.UTF8.GetString(bytes, (int)field.Source.ByteOffset, field.Source.ByteLength));
        }
    }

    [Fact]
    public async Task ParsesWrappedMessagesWithMetadataAndSourceOffsets()
    {
        var text = "\uFEFF" + Request + Reply;
        var result = await Parse(text);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.Records.Count);
        var first = result.Records[0];
        Assert.Equal("18223", first.Message!.RecordNumber);
        Assert.Equal("IN", first.Message.Direction);
        Assert.Equal("request", first.Message.Role);
        Assert.Equal("02794", first.Message.Sequence);
        Assert.Equal("TEST-ACCEPTOR", MessageAnalysis.Value(first, "42"));
        Assert.Equal("000346", MessageAnalysis.Value(first, "11"));
        foreach (var record in result.Records)
        foreach (var field in record.Fields)
        {
            var actual = Encoding.UTF8.GetBytes(text).AsSpan((int)field.Source.ByteOffset, field.Source.ByteLength).ToArray();
            Assert.Equal(record.RawText.Substring(field.Source.CharacterOffset, field.Source.CharacterLength), Encoding.UTF8.GetString(actual));
        }
        Assert.Equal(Request, first.RawText);
        Assert.DoesNotContain(first.Findings, f => f.Status == "Missing");
        Assert.DoesNotContain(result.Records[1].Findings, f => f.Field == "61" && f.Status == "Invalid");
    }

    [Fact]
    public async Task RetainsOperationAndDecodesIccAndReportsAmountMismatch()
    {
        var record = (await Parse(Request)).Records[0];
        var processing = record.Fields.Single(f => f.DataElement == "3");
        Assert.Equal("123456", processing.Value);
        Assert.Empty(processing.Children ?? []);
        var icc = record.Fields.Single(f => f.DataElement == "55");
        Assert.Equal(3, icc.Children!.Count);
        Assert.Empty(icc.Children.Single(f => f.Name == "9F02").Children ?? []);
        Assert.Contains(icc.Children, f => f.Name == "9F33" && f.Value == "E0F8C8");
        Assert.Contains(icc.Children.Single(f => f.Name == "9F33").Children!, f => f.Value.Contains("Terminal Capabilities"));
        Assert.Contains(record.Findings, f => f.RuleId == "emv.amount");
        Assert.Contains(record.Findings, f => f.Field == "55" && f.Status == "Undetermined" && f.Explanation.Contains("Synthetic"));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public async Task DecodedSubfieldsPointToOnlyTheirOwnOriginalValues(string newline)
    {
        var text = ("TEST RECORD 18223 FROM 17Jan 14:14:58.83 IN ON DEMO SEQ: 02794\n" +
            " Type=0200 Future-field=café Operation=123456\n" +
            " ICC=9A:03:240\n 117/9C:01:00/9A:03:240117/9F33:03:E0F8C8/\n Checksum=6579846300000000\n")
            .ReplaceLineEndings(newline);
        var record = Assert.Single((await Parse(text)).Records);
        var icc = record.Fields.Single(f => f.DataElement == "55");
        Assert.Equal(4, icc.Children!.Count);
        var bytes = Encoding.UTF8.GetBytes(text);
        foreach (var child in icc.Children)
        {
            var source = child.Source;
            var actual = record.RawText.Substring(source.CharacterOffset, source.CharacterLength);
            Assert.Equal(child.Value, string.Concat(actual.Where(c => !char.IsWhiteSpace(c))));
            Assert.Equal(actual, Encoding.UTF8.GetString(bytes, (int)source.ByteOffset, source.ByteLength));
            Assert.False(char.IsWhiteSpace(actual[0]));
            Assert.False(char.IsWhiteSpace(actual[^1]));
            Assert.Equal(':', record.RawText[source.CharacterOffset - 1]);
        }
        var dates = icc.Children.Where(f => f.Name == "9A").ToArray();
        Assert.True(dates[0].Source.CharacterOffset < dates[1].Source.CharacterOffset);
        var capabilities = icc.Children.Single(f => f.Name == "9F33");
        Assert.Equal(capabilities.Source, Assert.Single(capabilities.Children!).Source);
        foreach (var field in record.Fields)
        {
            var actual = record.RawText.Substring(field.Source.CharacterOffset, field.Source.CharacterLength);
            Assert.Equal(actual.Trim(), actual);
        }
    }

    [Fact]
    public async Task RetainsUnknownDuplicateAndMalformedFieldsWithoutLosingMessage()
    {
        var record = (await Parse(Request.Replace("ICC=", "Future-field=abc Trace=000347 ICC=")
            .Replace("9F02:06:", "9F02:07:"))).Records[0];
        Assert.Contains(record.Findings, f => f.Status == "Unknown" && f.Field == "Future-field");
        Assert.Contains(record.Findings, f => f.Status == "Duplicate" && f.Field == "11");
        Assert.Contains(record.Findings, f => f.RuleId == "emv.entry");
        Assert.Equal("000338", MessageAnalysis.Value(record, "61"));
        Assert.Contains(record.Fields.Single(f => f.DataElement == "55").Children!, f => f.Name == "9F33");
    }

    [Fact]
    public async Task MissingIsDistinctFromUnresolvedAndForbidden()
    {
        var record = (await Parse(Request.Replace("Trace=000346", "").Replace("Context=000338", "Conditional=abc"))).Records[0];
        Assert.Contains(record.Findings, f => f.Field == "11" && f.Status == "Missing");
        Assert.Contains(record.Findings, f => f.Field == "47" && f.Status == "Forbidden");
        Assert.Contains(record.Findings, f => f.Status == "Undetermined");
    }

    [Fact]
    public async Task SearchesNormalizedFieldsAndSortsAndFiltersMetadata()
    {
        var result = await Parse(Reply + "\r\n" + Request);
        Assert.Equal("18223", MessageAnalysis.Filter(result.Records)[0].Message!.RecordNumber);
        Assert.Equal("18224", MessageAnalysis.Filter(result.Records, sort: "datetime", descending: true)[0].Message!.RecordNumber);
        Assert.Single(MessageAnalysis.Filter(result.Records, direction: "IN"));
        Assert.Single(MessageAnalysis.Filter(result.Records, role: "reply"));
        Assert.Equal(2, MessageAnalysis.Filter(result.Records, "000346", "Trace").Count);
        Assert.Single(MessageAnalysis.Filter(result.Records, "96", "39"));
    }

    [Fact]
    public async Task CorrelationPreservesAmbiguityAndRejectsOtherConnections()
    {
        var result = await Parse(Request + Reply + "\r\n" + Reply.Replace("18224", "18225"));
        var parser = new RecordMessageParser(Profile());
        Assert.Equal(2, parser.Related(result.Records[0], result.Records).Count);
        var unrelated = await Parse(Request + Reply.Replace("OUT ON DEMO", "OUT ON 99999999"));
        Assert.Empty(parser.Related(unrelated.Records[0], unrelated.Records));
    }

    [Fact]
    public async Task ApplicabilityOnlyDoesNotInventRequiredPresence()
    {
        var text = "TEST RECORD 18222 FROM 17Jan 14:14:51.20 OUT ON DEMO SEQ: 02793\n" +
            "Type=0810 Operation=123456 Trace=000345 " +
            "Response=00 Terminal=DEMO Acceptor=TEST-ACCEPTOR";
        var record = (await Parse(text)).Records[0];
        Assert.DoesNotContain(record.Findings, f => f.Status == "Missing");
        Assert.Contains(record.Findings, f => f.Field == "61" && f.Status == "Undetermined");
    }

    [Fact]
    public void RejectsConflictingAliasesAndDanglingRules()
    {
        var p = Profile();
        Assert.Throws<ArgumentException>(() => (p with { Fields = [.. p.Fields, new("new", "New", ["Trace"])] }).Validate());
        p.Messages["0200"] = p.Messages["0200"] with { Fields = [new("does-not-exist", "M")] };
        Assert.Throws<ArgumentException>(p.Validate);
    }

    [Fact]
    public async Task AlternateLabelsAreConfigurableAndValuesKeepPadding()
    {
        var p = Profile();
        p.Fields[Array.FindIndex(p.Fields, f => f.Id == "11")] = new("11", "STAN", ["Custom trace"], "digits", 6);
        var parser = new RecordMessageParser(p);
        var text = Request.Replace("Trace", "Custom trace").Replace("Context=", "Padded=ABC  DEF Context=");
        var result = await parser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        Assert.Equal("000346", MessageAnalysis.Value(result.Records[0], "11"));
        Assert.Equal("ABC  DEF", MessageAnalysis.Value(result.Records[0], "60"));
    }

    [Fact]
    public async Task ProcessingCodeRequiresAnExtensionAndUsesItsResult()
    {
        var profile = Profile();
        var index = Array.FindIndex(profile.Fields, f => f.Id == "3");
        profile.Fields[index] = profile.Fields[index] with { Decoder = "processingCode" };
        var bytes = Encoding.UTF8.GetBytes(Request);
        var without = await new RecordMessageParser(profile).ParseAsync(new MemoryStream(bytes));
        var raw = without.Records[0].Fields.Single(f => f.DataElement == "3");
        Assert.Equal("123456", raw.Value);
        Assert.Empty(raw.Children ?? []);
        Assert.Contains(without.Records[0].Findings, f => f.Field == "3" && f.RuleId == "decode.unavailable");

        var withExtension = await new RecordMessageParser(profile, [new SyntheticOperationDecoder()])
            .ParseAsync(new MemoryStream(bytes));
        var decoded = withExtension.Records[0].Fields.Single(f => f.DataElement == "3");
        Assert.Equal("Extension result", Assert.Single(decoded.Children!).Value);
        Assert.DoesNotContain(withExtension.Records[0].Findings, f => f.RuleId == "decode.unavailable");
    }

    private sealed class SyntheticOperationDecoder : IFieldDecoder
    {
        public string Id => "processingCode";
        public FieldDecodeResult Decode(FieldDecodeContext context, CancellationToken cancellationToken) =>
            new([new("Synthetic operation", "Extension result", context.Field.Source)]);
    }

    [Fact]
    public async Task HonorsCancellationAndReportsNoHeaderAndInvalidEncoding()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var parser = new RecordMessageParser(Profile());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parser.ParseAsync(new MemoryStream(Encoding.UTF8.GetBytes(Request)), cts.Token));
        Assert.Contains((await Parse("not a message")).Diagnostics, d => d.Code == "parse.header");
        Assert.Contains((await parser.ParseAsync(new MemoryStream([255]))).Diagnostics, d => d.Code == "parse.encoding");
        Assert.True(parser.CanParse(Encoding.UTF8.GetBytes(Request)));
    }
}
