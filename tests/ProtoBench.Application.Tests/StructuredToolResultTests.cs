using ProtoBench.Application.Workspaces;
using ProtoBench.PluginSdk;
using ProtoBench.Infrastructure.Plugins;

namespace ProtoBench.Application.Tests;

public sealed class StructuredToolResultTests
{
    private static readonly ToolSearchResult Search = new("response code",
        [new("DE39", "Outcome", "00 means approved") { Score = 3.5, Reference = new("Synthetic spec", "§ 1") }]);

    [Fact]
    public async Task PackagedExampleLoadsAgainstHostContractsAndExecutesBothViews()
    {
        var directory = Path.Combine(Path.GetTempPath(), "structured-plugin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "source"); Directory.CreateDirectory(source);
            File.Copy(typeof(StructuredResultsPlugin.Plugin).Assembly.Location, Path.Combine(source, "StructuredResultsPlugin.dll"));
            var manifest = new PluginManifest(1, PluginApi.Version, "example.structured-results", "Structured examples", "1.0.0",
                "StructuredResultsPlugin.dll", "StructuredResultsPlugin.Plugin", ["StructuredResultsPlugin.dll"]);
            var manifestPath = Path.Combine(source, "plugin.json");
            File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(manifest));
            var packages = new PluginPackageManager(Path.Combine(directory, "installed")); packages.Import(manifestPath);
            using var catalog = PluginCatalog.Load(packages, [], [], []);
            Assert.Empty(catalog.Diagnostics);
            var tools = Assert.Single(catalog.Plugins).Tools;
            using var search = new PluginToolWorkspace(tools.Single(t => t.Id == "example.spec-search"));
            search.SetInput("max", "1");
            await search.ExecuteAsync();
            Assert.Single(search.Result!.Search!.Hits);
            Assert.Equal(2, search.Result.Search.TotalMatches);
            search.SetInput("query", "no match"); await search.ExecuteAsync();
            Assert.Empty(search.Result!.Search!.Hits);
            using var decoder = new PluginToolWorkspace(tools.Single(t => t.Id == "example.spec-field"));
            decoder.SetInput("other", "{\"DE3\":\"000000\"}"); await decoder.ExecuteAsync();
            Assert.Equal(ToolDecodeStatus.Decoded, decoder.Result!.Decoding!.Status);
            Assert.Single(decoder.Result.Decoding.Warnings);
            decoder.SetInput("value", "99"); await decoder.ExecuteAsync();
            Assert.Equal(ToolDecodeStatus.Unmapped, decoder.Result!.Decoding!.Status);
            Assert.Equal("99", Assert.Single(decoder.Result.Decoding.Components).RawValue);
        }
        finally
        {
            // The collectible plugin context may keep its DLL mapped until a later GC.
            try { Directory.Delete(directory, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public async Task SearchWithNoRawTextIsStillAResultAndInputEditsInvalidateIt()
    {
        using var workspace = Create(new PluginToolResult("") { Search = Search }, [new("query", "Query")]);
        await workspace.ExecuteAsync();
        Assert.Same(Search, workspace.Result!.Search);
        Assert.Empty(workspace.Output);
        workspace.SetInput("query", "new search");
        Assert.Null(workspace.Result);
        Assert.Empty(workspace.Fields);
        Assert.Contains("Inputs changed", workspace.Status);
    }

    [Fact]
    public async Task UnchangedInputPreservesResultAndClearResetsStructuredOutput()
    {
        using var workspace = Create(new PluginToolResult("raw") { Search = Search }, [new("query", "Query", DefaultValue: "original")]);
        await workspace.ExecuteAsync();
        workspace.SetInput("query", "original");
        Assert.NotNull(workspace.Result);
        workspace.Clear();
        Assert.Null(workspace.Result);
        Assert.Empty(workspace.Output);
        Assert.Equal("original", workspace.Inputs["query"]);
    }

    [Fact]
    public async Task DecoderPreservesUnknownValuesWarningsAndProvenance()
    {
        var decoding = new ToolDecodeResult("DE22", "991", "Partly mapped",
            [new("Entry method", "99", "Unmapped") { Position = "Digits 1–2", Children = [new("Original", "99", "Unknown")] }])
        {
            Status = ToolDecodeStatus.PartiallyDecoded,
            Warnings = ["Entry method is not mapped."],
            ContextUsed = [new("MTI", "0200")], References = [new("Example v1", "Page 1", "Synthetic passage")]
        };
        using var workspace = Create(new PluginToolResult("raw") { Decoding = decoding });
        await workspace.ExecuteAsync();
        Assert.Same(decoding, workspace.Result!.Decoding);
        workspace.Dispose();
        Assert.Null(workspace.Result);
    }

    [Fact]
    public async Task ConflictingStructuredPresentationsFailWithoutDisplayingOldResult()
    {
        using var workspace = Create(new PluginToolResult("raw")
        { Search = Search, Decoding = new("DE39", "00", "Approved", []) });
        await workspace.ExecuteAsync();
        Assert.Null(workspace.Result);
        Assert.Empty(workspace.Output);
        Assert.Contains("both search and decoding", workspace.Status);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("1.5")]
    [InlineData("not a number")]
    public async Task InvalidIntegerDoesNotInvokePlugin(string value)
    {
        var session = new ResultSession(new("unused"));
        using var workspace = new PluginToolWorkspace(new("test", "Test", "",
            [new("max", "Max results", ToolInputKind.Integer, Required: true) { Minimum = 1, Maximum = 100 }], () => session));
        workspace.SetInput("max", value);
        await workspace.ExecuteAsync();
        Assert.Equal(0, session.Calls);
        Assert.Contains("Max results must be an integer", workspace.Status);
    }

    [Theory]
    [InlineData("{\"DE3\":\"001000\",\"de3\":\"000000\"}")]
    [InlineData("{\"\":\"000000\"}")]
    [InlineData("{\"DE3\":1000}")]
    [InlineData("[]")]
    public async Task InvalidSupportingFieldsDoNotInvokePlugin(string value)
    {
        var session = new ResultSession(new("unused"));
        using var workspace = new PluginToolWorkspace(new("test", "Test", "",
            [new("context", "Other fields", ToolInputKind.FieldValues)], () => session));
        workspace.SetInput("context", value);
        await workspace.ExecuteAsync();
        Assert.Equal(0, session.Calls);
        Assert.Null(workspace.Result);
        Assert.NotEqual("Completed", workspace.Status);
    }

    [Fact]
    public void SupportingFieldsPreserveValueWhitespaceAndLeadingZeroes()
    {
        var fields = ToolInputValues.ParseFieldValues("{\" DE3 \":\"001000\",\"DE41\":\" ABC  \"}");
        Assert.Equal("001000", fields["de3"]);
        Assert.Equal(" ABC  ", fields["DE41"]);
        Assert.Empty(ToolInputValues.ParseFieldValues(""));
    }

    [Fact]
    public async Task CancellationCannotPublishStructuredResultsFromNonCooperativeSession()
    {
        var session = new DelayedSession();
        using var workspace = new PluginToolWorkspace(new("test", "Test", "", [new("q", "Query")], () => session));
        var operation = workspace.ExecuteAsync();
        await session.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        workspace.SetInput("q", "ignored while running");
        Assert.Empty(workspace.Inputs["q"]);
        workspace.Cancel(); session.Release.SetResult();
        await operation;
        Assert.Null(workspace.Result);
        Assert.Equal("Cancelled", workspace.Status);
    }

    private static PluginToolWorkspace Create(PluginToolResult result, IReadOnlyList<ToolInput>? inputs = null) =>
        new(new("test", "Test", "", inputs ?? [], () => new ResultSession(result)));

    private sealed class ResultSession(PluginToolResult result) : IPluginToolSession
    {
        public int Calls { get; private set; }
        public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
        { Calls++; return Task.FromResult(result); }
        public void Dispose() { }
    }

    private sealed class DelayedSession : IPluginToolSession
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
        { Started.SetResult(); await Release.Task; return new("") { Search = Search }; }
        public void Dispose() { }
    }
}
