using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;
using ProtoBench.Desktop.Features.Parsers;
using ProtoBench.Parsing;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;
using ProtoBench.Parsing.Registry;

namespace ProtoBench.Application.Tests;

public sealed class MessageCompareWorkspaceTests
{
    private static MessageCompareWorkspace Workspace() => new(new ParserRegistry([new JsonDefinedParser(
        ParserDefinition.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/parsers/synthetic-delimited.json"))))]));

    [Fact]
    public async Task ComparesUsingSelectedSpecAndClearsStaleResults()
    {
        using var vm = Workspace();
        vm.SelectedFormatId = "synthetic-delimited";
        vm.Message1 = "SYN|hello|1"; vm.Message2 = "SYN|hello|2";
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Single(vm.Rows, r => r.Status == FieldComparisonStatus.Changed);
        vm.DifferencesOnly = true;
        Assert.Single(vm.VisibleRows);
        vm.Message2 = "SYN|hello|1";
        Assert.Empty(vm.Rows);
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Empty(vm.VisibleRows);
        Assert.Contains("0 differ", vm.Summary);
        vm.SelectedFormatId = null;
        Assert.Empty(vm.Rows);
    }

    [Theory]
    [InlineData(null, "SYN|hello|1", "SYN|hello|1", "Select a spec")]
    [InlineData("synthetic-delimited", "", "SYN|hello|1", "Message 1 and Message 2")]
    [InlineData("synthetic-delimited", "SYN|hello|1\nSYN|world|2", "SYN|hello|1", "same number")]
    [InlineData("synthetic-delimited", "SYN|hello|1", "SYN|hello|1\nSYN|world|2", "same number")]
    [InlineData("synthetic-delimited", "SYN|hello|1\nSYN|world|2\nSYN|extra|3", "SYN|hello|1", "expected one request")]
    public async Task ValidatesInputs(string? spec, string a, string b, string expected)
    {
        using var vm = Workspace();
        vm.SelectedFormatId = spec; vm.Message1 = a; vm.Message2 = b;
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Empty(vm.Rows);
        Assert.Contains(expected, vm.Status);
    }

    [Fact]
    public async Task ExplainsMissingSpecs()
    {
        using var vm = new MessageCompareWorkspace(new ParserRegistry([]));
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Contains("No parser specs are installed", vm.Status);
    }

    [Fact]
    public async Task ComparesRequestsAndRepliesSeparatelyAndCanReturnToSingleMessage()
    {
        using var vm = Workspace();
        vm.SelectedFormatId = "synthetic-delimited";
        vm.Message1 = "SYN|request|1\nSYN|reply|2";
        vm.Message2 = "SYN|request|3\nSYN|reply|4";
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(6, vm.Rows.Count);
        Assert.Contains("Request + reply", vm.Summary);
        Assert.Contains("4 match · 2 differ", vm.Summary);
        vm.DifferencesOnly = true;
        var differences = vm.VisibleRows.ToArray();
        Assert.Equal(2, differences.Length);
        Assert.Equal("Request / count", differences[0].Path);
        Assert.Equal("1", differences[0].Message1);
        Assert.Equal("3", differences[0].Message2);
        Assert.Equal("Reply / count", differences[1].Path);
        Assert.Equal("2", differences[1].Message1);
        Assert.Equal("4", differences[1].Message2);

        vm.Message1 = vm.Message2 = "SYN|request|1";
        Assert.Empty(vm.Rows);
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Contains("Single message", vm.Summary);
        Assert.Empty(vm.VisibleRows);
        Assert.All(vm.Rows, row => Assert.DoesNotContain(" / ", row.Path));
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Summary);
        Assert.Empty(vm.Message1);
        Assert.Empty(vm.Message2);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public async Task ComparesRecordPairsIncludingDecodedReplySubfields(string newline)
    {
        var profile = MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json")));
        using var vm = new MessageCompareWorkspace(new ParserRegistry([new RecordMessageParser(profile)]));
        vm.SelectedFormatId = profile.Id;
        vm.Message1 = $"Type=0200 Amount=000000000100{newline}Type=0210 ICC=9F02:06:000000000100/";
        vm.Message2 = $"Type=0200 Amount=000000000100{newline}Type=0210 ICC=9F02:06:000000000200/ Response=00";
        await vm.CompareCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Rows);
        Assert.All(vm.Rows.Where(row => row.Path.StartsWith("Request / ")), row => Assert.Equal(FieldComparisonStatus.Match, row.Status));
        Assert.Contains(vm.Rows, row => row.Path == "Reply / 55" && row.Status == FieldComparisonStatus.Changed);
        Assert.Contains(vm.Rows, row => row.Path == "Reply / 55 / 9F02" && row.Status == FieldComparisonStatus.Changed);
        Assert.Contains(vm.Rows, row => row.Path == "Reply / 39" && row.Status == FieldComparisonStatus.MissingFromMessage1);
        Assert.Contains("Message 1:", vm.Diagnostics);
        Assert.Contains("Message 2:", vm.Diagnostics);
    }

    private sealed class DelayedParser : ILogParser
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string FormatId => "delayed";
        public string Name => "Delayed";
        public int DetectionPriority => 0;
        public bool CanParse(ReadOnlySpan<byte> sample) => true;
        public async Task<ParseResult> ParseAsync(Stream input, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Resume.Task.WaitAsync(cancellationToken);
            return new([new ParsedRecord(1, "", [new ParsedField("field", "value", new(0, 0, 0, 0))])], []);
        }
    }

    [Fact]
    public async Task EditingDuringParsingDiscardsOldComparison()
    {
        var parser = new DelayedParser();
        using var vm = new MessageCompareWorkspace(new ParserRegistry([parser]));
        vm.SelectedFormatId = parser.FormatId; vm.Message1 = vm.Message2 = "before";
        var work = vm.CompareCommand.ExecuteAsync(null);
        await parser.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.Message2 = "after";
        parser.Resume.SetResult();
        await work;
        Assert.Empty(vm.Rows);
        Assert.Empty(vm.Summary);
    }
}
