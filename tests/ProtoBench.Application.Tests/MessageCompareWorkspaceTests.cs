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

    [Fact]
    public async Task CombinesMessageFieldAndDifferenceFilters()
    {
        using var vm = Workspace();
        vm.DifferencesOnly = false;
        vm.SelectedFormatId = "synthetic-delimited";
        vm.Message1 = "SYN|request|1\nSYN|reply|2";
        vm.Message2 = "SYN|request|3\nSYN|reply|4";
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Fields.Count);
        vm.ShowReply = false;
        Assert.Equal(3, vm.VisibleRows.Count());
        Assert.All(vm.VisibleRows, row => Assert.StartsWith("Request / ", row.Path));
        vm.ShowReply = true;
        vm.ShowRequest = false;
        Assert.All(vm.VisibleRows, row => Assert.StartsWith("Reply / ", row.Path));
        vm.ShowReply = false;
        Assert.Empty(vm.VisibleRows);
        vm.ShowRequest = vm.ShowReply = true;
        vm.SelectAllFields(false);
        Assert.Empty(vm.VisibleRows);
        vm.Fields.Single(field => field.Path == "count").IsSelected = true;
        Assert.Equal(2, vm.VisibleRows.Count());
        vm.DifferencesOnly = true;
        vm.ShowReply = false;
        Assert.Equal("Request / count", Assert.Single(vm.VisibleRows).Path);
        vm.FieldSearch = "COUNT";
        Assert.Equal("count", Assert.Single(vm.VisibleFields).Path);
        vm.FieldSearch = "no such field";
        Assert.Empty(vm.VisibleFields);
        Assert.Single(vm.VisibleRows); // Searching choices does not change selected fields.
        vm.Message1 = vm.Message2 = "SYN|single|1";
        Assert.Empty(vm.Fields);
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.All(vm.Fields, field => Assert.True(field.IsSelected));
        vm.DifferencesOnly = false;
        Assert.Equal(3, vm.VisibleRows.Count());
        vm.ShowRequest = false;
        Assert.Empty(vm.VisibleRows);
    }

    [Fact]
    public async Task CanSelectDecodedSubfieldWithoutItsParentAndSearchByName()
    {
        var profile = MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json")));
        using var vm = new MessageCompareWorkspace(new ParserRegistry([new RecordMessageParser(profile)]));
        vm.SelectedFormatId = profile.Id;
        vm.Message1 = "Type=0200 Amount=000000000100\nType=0210 ICC=9F02:06:000000000100/";
        vm.Message2 = "Type=0200 Amount=000000000100\nType=0210 ICC=9F02:06:000000000200/";
        await vm.CompareCommand.ExecuteAsync(null);
        vm.SelectAllFields(false);
        var subfield = vm.Fields.Single(field => field.Path == "55 / 9F02");
        subfield.IsSelected = true;
        Assert.Equal("Reply / 55 / 9F02", Assert.Single(vm.VisibleRows).Path);
        vm.FieldSearch = subfield.Name.ToUpperInvariant();
        Assert.Contains(subfield, vm.VisibleFields);
        vm.ClearCommand.Execute(null);
        Assert.Empty(vm.Fields);
        Assert.Empty(vm.VisibleRows);
    }

    [Fact]
    public async Task SearchesMultipleParsedFieldsAndSelectsMatchesTogether()
    {
        var profile = MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json")));
        using var vm = new MessageCompareWorkspace(new ParserRegistry([new RecordMessageParser(profile)]));
        vm.SelectedFormatId = profile.Id;
        vm.Message1 = "Type=0200 Amount=000000000100\nType=0210 ICC=9F02:06:000000000100/";
        vm.Message2 = "Type=0200 Amount=000000000100\nType=0210 ICC=9F02:06:000000000200/ Response=00";
        await vm.CompareCommand.ExecuteAsync(null);
        // Include fields from either input, reply-only fields and decoded subfields.
        Assert.Contains(vm.Fields, option => option.Path == "39");
        Assert.Contains(vm.Fields, option => option.Path == "55 / 9F02");
        vm.FieldSearch = " 39, 9f02; unknown\r\n , ;";
        Assert.Equal(new[] { "39", "55 / 9F02" }, vm.VisibleFields.Select(option => option.Path).OrderBy(path => path));
        vm.SelectMatchingFields();
        Assert.Equal(2, vm.VisibleRows.Count());
        Assert.All(vm.VisibleRows, row => Assert.True(row.Path is "Reply / 39" or "Reply / 55 / 9F02"));
        vm.FieldSearch = "";
        Assert.Equal(vm.Fields.Count, vm.VisibleFields.Count());
        Assert.Equal(2, vm.VisibleRows.Count()); // Clearing the search preserves selection.
        vm.FieldSearch = " ; , \r\n ";
        Assert.Equal(vm.Fields.Count, vm.VisibleFields.Count());
        vm.FieldSearch = vm.Fields.Single(option => option.Path == "55 / 9F02").Name;
        Assert.Contains(vm.Fields.Single(option => option.Path == "55 / 9F02"), vm.VisibleFields);
        vm.FieldSearch = "not a parsed field";
        vm.SelectMatchingFields();
        Assert.Empty(vm.VisibleRows);
        vm.Message1 = vm.Message2 = "Type=0200 Amount=000000000100";
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.DoesNotContain(vm.Fields, option => option.Path is "39" or "55 / 9F02");
    }

    [Fact]
    public async Task DefaultsToDifferencesAndCanHideSubfieldsWithoutLosingSelections()
    {
        var profile = MessageProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json")));
        using var vm = new MessageCompareWorkspace(new ParserRegistry([new RecordMessageParser(profile)]));
        Assert.True(vm.DifferencesOnly);
        Assert.True(vm.ShowSubfields);
        vm.SelectedFormatId = profile.Id;
        vm.Message1 = "Type=0200 Amount=000000000100\nType=0210 ICC=9F02:06:000000000100/";
        vm.Message2 = "Type=0200 Amount=000000000100\nType=0210 ICC=9F02:06:000000000200/";
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.All(vm.VisibleRows, row => Assert.NotEqual(FieldComparisonStatus.Match, row.Status));
        Assert.Contains(vm.VisibleRows, row => row.Path == "Reply / 55 / 9F02");
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        vm.ShowSubfields = false;
        Assert.Contains(nameof(vm.VisibleRows), notifications);
        Assert.All(vm.VisibleRows, row => Assert.Equal(0, row.Depth));
        Assert.Contains(vm.VisibleRows, row => row.Path == "Reply / 55");
        Assert.True(vm.Fields.Single(option => option.Path == "55 / 9F02").IsSelected);
        vm.DifferencesOnly = false;
        Assert.Contains(vm.VisibleRows, row => row.Status == FieldComparisonStatus.Match);
        Assert.All(vm.VisibleRows, row => Assert.Equal(0, row.Depth));
        vm.ShowSubfields = true;
        Assert.Contains(vm.VisibleRows, row => row.Path == "Reply / 55 / 9F02");
        vm.ShowReply = false;
        Assert.All(vm.VisibleRows, row => Assert.StartsWith("Request / ", row.Path));
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.False(vm.DifferencesOnly); // Respect an explicit change on subsequent comparisons.
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
