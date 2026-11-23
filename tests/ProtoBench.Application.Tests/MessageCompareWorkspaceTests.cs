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
    [InlineData("synthetic-delimited", "", "SYN|hello|1", "each message box")]
    [InlineData("synthetic-delimited", "SYN|hello|1\nSYN|world|2", "SYN|hello|1", "exactly one")]
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
