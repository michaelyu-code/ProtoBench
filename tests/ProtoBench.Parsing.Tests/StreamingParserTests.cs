using System.Text;
using ProtoBench.Core.Diagnostics;
using ProtoBench.Core.Documents;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Parsing.Tests;

public sealed class StreamingParserTests
{
    private sealed class Sink : ILogParseSink
    {
        public List<ParsedRecord> Records = [];
        public List<ParseDiagnostic> Diagnostics = [];
        public void AddRecord(ParsedRecord record) => Records.Add(record);
        public void AddDiagnostic(ParseDiagnostic diagnostic) => Diagnostics.Add(diagnostic);
        public void ReportProgress(long bytesRead) { }
    }
    private static JsonDefinedParser Lines() => new(new ParserDefinition
    {
        Id = "lines", Name = "Lines", Fields = [new() { Name = "kind" }, new() { Name = "value" }, new() { Name = "number" }]
    });
    private static RecordMessageParser Messages() => new(MessageProfile.FromJson(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json"))));

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task HeaderlessMessagesRecoverBeforeBetweenAndAfterNormalMessages(string newline)
    {
        var text = ("\uFEFFignored preamble\n Type=0200 Trace=111111 Terminal=café\n" +
            "TEST RECORD 2 FROM 17Jan 14:14:58.83 IN ON DEMO SEQ: 00002\n" +
            " Type=0200 Trace=222222 Terminal=café\n" +
            " Type=0210 Trace=333333 Terminal=café\n" +
            "TEST RECORD 4 FROM 17Jan 14:14:58.84 OUT ON DEMO SEQ: 00004\n" +
            " Type=0210 Trace=444444 Terminal=café\n" +
            " Type=0200 Trace=555555 Terminal=café").ReplaceLineEndings(newline);
        var bytes = Encoding.UTF8.GetBytes(text);
        var preview = await Messages().ParseAsync(new MemoryStream(bytes));
        var sink = new Sink();
        await Messages().ParseToAsync(new MemoryStream(bytes), sink);
        Assert.Equal(5, preview.Records.Count);
        Assert.Equal(5, sink.Records.Count);
        Assert.Equal(3, sink.Diagnostics.Count(d => d.Code == "parse.header-missing"));
        Assert.Equal(3, preview.Diagnostics.Count(d => d.Code == "parse.header-missing"));
        Assert.Equal(new[] { "111111", "222222", "333333", "444444", "555555" }, sink.Records.Select(r => MessageAnalysis.Value(r, "11")));
        foreach (var (record, index) in sink.Records.Select((r, i) => (r, i)))
        {
            Assert.Equal(preview.Records[index].RawText, record.RawText);
            Assert.Equal(preview.Records[index].Message, record.Message);
            Assert.Equal(preview.Records[index].LineNumber, record.LineNumber);
            Assert.Equal(preview.Records[index].Fields, record.Fields);
            if (index is 0 or 2 or 4)
            {
                Assert.Equal("", record.Message!.RecordNumber);
                Assert.Equal("unknown", record.Message.Direction);
                Assert.Equal("unknown", record.Message.LoggedDateTime);
                Assert.Equal("unknown", record.Message.Sequence);
                Assert.Contains(record.Findings, f => f.RuleId == "message.header-missing");
            }
            else Assert.DoesNotContain(record.Findings, f => f.RuleId == "message.header-missing");
            foreach (var field in record.Fields)
                Assert.Equal(record.RawText.Substring(field.Source.CharacterOffset, field.Source.CharacterLength),
                    Encoding.UTF8.GetString(bytes, (int)field.Source.ByteOffset, field.Source.ByteLength));
        }
    }

    [Fact]
    public async Task HeaderlessDetectionRequiresALineStartMtiAndKnownMessageRules()
    {
        var parser = Messages();
        Assert.True(parser.CanParse(Encoding.UTF8.GetBytes("Type=0200 Trace=123456")));
        Assert.False(parser.CanParse(Encoding.UTF8.GetBytes("Some text Type=0200")));
        Assert.False(parser.CanParse(Encoding.UTF8.GetBytes("Type=02000 Trace=123456")));
        Assert.False(parser.CanParse(Encoding.UTF8.GetBytes("Type=9999 Trace=123456")));
        var sink = new Sink();
        await parser.ParseToAsync(new MemoryStream(Encoding.UTF8.GetBytes("Type=9999 Trace=123456")), sink);
        var record = Assert.Single(sink.Records);
        Assert.Contains(record.Findings, f => f.RuleId == "message.unknown");
        Assert.Contains(record.Findings, f => f.RuleId == "message.header-missing");
    }

    [Fact]
    public async Task StreamsPastBothOldPreviewLimitsAndPreservesFinalUnterminatedRecord()
    {
        var sink = new Sink();
        var line = "SYN|" + new string('x', 1700) + "|1\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, 10001)) + "SYN|last|2"));
        await Lines().ParseToAsync(input, sink);
        Assert.Equal(10002, sink.Records.Count);
        Assert.Equal("last", sink.Records[^1].Fields[1].Value);
        Assert.Empty(sink.Diagnostics);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task StreamingMessagesMatchPreviewIncludingUnicodeAndOffsets(string newline)
    {
        var text = ("\uFEFFpreamble\nTEST RECORD 1 FROM 17Jan 14:14:58.83 IN ON DEMO SEQ: 00001\n" +
            " Type=0200 Trace=123456 Terminal=café\n\nTEST RECORD 2 FROM 17Jan 14:14:58.84 OUT ON DEMO SEQ: 00002\n" +
            " Type=0210 Trace=123456 Terminal=café").ReplaceLineEndings(newline);
        var bytes = Encoding.UTF8.GetBytes(text);
        var preview = await Messages().ParseAsync(new MemoryStream(bytes));
        var sink = new Sink();
        await Messages().ParseToAsync(new MemoryStream(bytes), sink);
        Assert.Equal(preview.Records.Count, sink.Records.Count);
        for (int i = 0; i < sink.Records.Count; i++)
        {
            var record = sink.Records[i];
            Assert.Equal(preview.Records[i].RawText, record.RawText);
            Assert.Equal(preview.Records[i].LineNumber, record.LineNumber);
            Assert.Equal(preview.Records[i].Fields, record.Fields);
            foreach (var field in record.Fields)
                Assert.Equal(record.RawText.Substring(field.Source.CharacterOffset, field.Source.CharacterLength),
                    Encoding.UTF8.GetString(bytes, (int)field.Source.ByteOffset, field.Source.ByteLength));
        }
    }

    [Fact]
    public async Task CrLfAcrossReadBufferAndBomHaveExactOffsets()
    {
        // First line ends with CR at the final byte of the 64 KiB input buffer.
        var text = "\uFEFFSYN|" + new string('x', 65526) + "|1\r\nSYN|café|2\rSYN|last|3";
        var bytes = Encoding.UTF8.GetBytes(text);
        Assert.Equal(13, bytes[65535]);
        var sink = new Sink();
        await Lines().ParseToAsync(new MemoryStream(bytes), sink);
        Assert.Equal(3, sink.Records.Count);
        Assert.Equal(3, sink.Records[0].Fields[0].Source.ByteOffset);
        Assert.Equal(65537, sink.Records[1].Fields[0].Source.ByteOffset);
        Assert.Equal("last", sink.Records[2].Fields[1].Value);
    }

    [Fact]
    public async Task OversizedAndInvalidLinesAreSkippedAndFollowingRecordsSurvive()
    {
        var sink = new Sink();
        using var input = new MemoryStream();
        await input.WriteAsync(Encoding.UTF8.GetBytes(new string('x', 4 * 1024 * 1024 + 1) + "\r\n"));
        await input.WriteAsync(new byte[] { 255, 10 });
        await input.WriteAsync("SYN|last|2"u8.ToArray());
        input.Position = 0;
        await Lines().ParseToAsync(input, sink);
        Assert.Equal("last", Assert.Single(sink.Records).Fields[1].Value);
        Assert.Contains(sink.Diagnostics, d => d.Code == "parse.line-limit");
        Assert.Contains(sink.Diagnostics, d => d.Code == "parse.record");
    }

    [Fact]
    public async Task CancellationStopsStreaming()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Lines().ParseToAsync(new MemoryStream("SYN|x|1"u8.ToArray()), new Sink(), cancellation.Token));
    }

    [Fact]
    public async Task StreamingRecordMessagesContinuePastTenThousandAndKeepFinalMessage()
    {
        var sink = new Sink();
        var message = "TEST RECORD 1 FROM 17Jan 14:14:58.83 IN ON DEMO SEQ: 00001\r\n Type=0200 Trace=123456 Terminal=TEST\r\n";
        var input = string.Concat(Enumerable.Repeat(message, 10001)) + "TEST RECORD 99999 FROM 17Jan 14:14:58.84 OUT ON DEMO SEQ: 00002\n Type=0210";
        await Messages().ParseToAsync(new MemoryStream(Encoding.UTF8.GetBytes(input)), sink);
        Assert.Equal(10002, sink.Records.Count);
        Assert.Equal("99999", sink.Records[^1].Message!.RecordNumber);
        Assert.DoesNotContain(sink.Diagnostics, d => d.Code.Contains("limit"));
    }
}
