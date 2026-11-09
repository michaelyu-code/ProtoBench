using Microsoft.Extensions.Logging.Abstractions;
using ProtoBench.Core.Documents;
using ProtoBench.Desktop.Features.Parsers;
using ProtoBench.Desktop.Services;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;
using ProtoBench.Parsing.Registry;

namespace ProtoBench.Application.Tests;

public sealed class ParserInputTests
{
    private sealed class Store : ISettingsStore
    {
        public int Saves;
        private LocalSettings value = new();
        public Task<LocalSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task SaveAsync(LocalSettings settings, CancellationToken cancellationToken = default) { value = settings; Saves++; return Task.CompletedTask; }
    }
    private static ParserWorkspace Workspace(Store store) => new(new ParserRegistry([new JsonDefinedParser(
        ParserDefinition.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions/parsers/synthetic-delimited.json"))))]),
        new SessionSettings(store), NullLogger<ParserWorkspace>.Instance);

    [Fact]
    public async Task AutoScrollPreferencePersistsAndAppliesToOpenParserWorkspaces()
    {
        var store = new Store();
        var settings = new SessionSettings(store);
        await settings.LoadAsync();
        using var workspace = new ParserWorkspace(new ParserRegistry([]), settings, NullLogger<ParserWorkspace>.Instance);
        var autoFitNotifications = 0;
        workspace.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(workspace.AutoFitResults)) autoFitNotifications++; };
        Assert.True(workspace.AutoScrollResults);
        Assert.True(workspace.AutoFitResults);
        await settings.SetParserAutoScrollAsync(false);
        await workspace.SetAutoFitResultsAsync(false);
        Assert.False(workspace.AutoScrollResults);
        Assert.False(workspace.AutoFitResults);
        var reloaded = new SessionSettings(store);
        await reloaded.LoadAsync();
        Assert.False(reloaded.Current.AutoScrollParserResults);
        Assert.False(reloaded.Current.AutoFitParserResults);
        Assert.True(autoFitNotifications > 0);
    }

    [Fact]
    public async Task NoInstalledSpecsExplainsHowToAddAParserExtension()
    {
        var store = new Store();
        using var workspace = new ParserWorkspace(new ParserRegistry([]), new SessionSettings(store), NullLogger<ParserWorkspace>.Instance);
        await workspace.ParseTextAsync("SYN|ok|1");
        Assert.Null(workspace.Page);
        Assert.Contains("No parser specs are installed", workspace.Status);
        Assert.Contains("Import a parser extension", workspace.Status);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task HeaderlessPasteAndFileAreDetectedAndIndexedAsSeparateMessages()
    {
        var parser = new RecordMessageParser(MessageProfile.FromJson(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "definitions/messages/synthetic-record-v1.json"))));
        using var workspace = new ParserWorkspace(new ParserRegistry([parser]), new SessionSettings(new Store()), NullLogger<ParserWorkspace>.Instance);
        var input = "Type=0200 Trace=123456 Terminal=café\r\nType=0210 Trace=123456 Terminal=café";
        await workspace.ParseTextAsync(input);
        Assert.Equal(2, workspace.Page!.Total);
        Assert.Contains("0 errors · 2 warnings", workspace.DiagnosticSummary);
        var record = await workspace.GetRecordAsync(workspace.Page.Rows[1].Id);
        Assert.Equal("reply", record.Message!.Role);
        Assert.Equal("unknown", record.Message.Direction);
        Assert.Contains(record.Findings, f => f.RuleId == "message.header-missing");
        var file = System.IO.Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, input);
            await workspace.OpenAsync(file);
            Assert.Equal(2, workspace.Page!.Total);
            Assert.Contains("Recovered message", workspace.Diagnostics);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task VerboseChecksDefaultOffPersistAndNotifyOpenWorkspaces()
    {
        var store = new Store();
        var settings = new SessionSettings(store);
        using var workspace = new ParserWorkspace(new ParserRegistry([]), settings, NullLogger<ParserWorkspace>.Instance);
        var notifications = 0;
        workspace.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(workspace.VerboseChecks)) notifications++; };
        Assert.False(workspace.VerboseChecks);
        await settings.SetVerboseParserChecksAsync(true);
        Assert.True(workspace.VerboseChecks);
        Assert.Equal(1, notifications);
        var reloaded = new SessionSettings(store);
        await reloaded.LoadAsync();
        Assert.True(reloaded.Current.VerboseParserChecks);
        await settings.SetVerboseParserChecksAsync(false);
        Assert.False(workspace.VerboseChecks);
    }

    [Fact]
    public async Task PastePreservesUnicodeWhitespaceAndNeverWritesRecentFiles()
    {
        var store = new Store();
        using var workspace = Workspace(store);
        await workspace.ParseTextAsync("SYN| café |7\r\nSYN|two|2");
        Assert.Equal("Completed", workspace.Status);
        Assert.Equal(2, workspace.Page!.Total);
        var record = await workspace.GetRecordAsync(workspace.Page.Rows[0].Id);
        Assert.Equal("SYN| café |7", record.RawText);
        Assert.Equal("café", record.Fields[1].Value);
        Assert.Equal(0, store.Saves);
        Assert.Equal("Pasted text (UTF-8)", workspace.Path);
    }

    [Fact]
    public async Task EmptyOrUndetectedPasteClearsPreviousResultsAndReportsError()
    {
        using var workspace = Workspace(new Store());
        await workspace.ParseTextAsync("SYN|ok|1");
        Assert.NotNull(workspace.Page);
        await workspace.ParseTextAsync(" \r\n");
        Assert.Null(workspace.Page);
        Assert.Contains("Paste log text", workspace.Status);
        await workspace.ParseTextAsync("unknown");
        Assert.Null(workspace.Page);
        Assert.Contains("No format detected", workspace.Status);
    }

    [Fact]
    public async Task PasteHonorsManualFormatAndCanFollowFileInput()
    {
        var store = new Store();
        using var workspace = Workspace(store);
        await workspace.OpenAsync(Path.Combine(AppContext.BaseDirectory, "samples/synthetic-delimited.txt"));
        Assert.Equal(1, store.Saves);
        workspace.SelectedFormatId = workspace.Parsers[0].FormatId;
        await workspace.ParseTextAsync("ALT|ok|1");
        Assert.Equal("Completed", workspace.Status);
        Assert.Single(workspace.Page!.Rows);
        Assert.Equal(1, store.Saves);
        Assert.Contains("manual selection", workspace.Candidates);
    }

    [Fact]
    public async Task LargePasteIndexesAllRecordsAndPagesBy2000()
    {
        using var workspace = Workspace(new Store());
        var line = "SYN|" + new string('x', 1800) + "|1\n";
        await workspace.ParseTextAsync(string.Concat(Enumerable.Repeat(line, 10001)) + "SYN|last marker|2");
        Assert.Equal("Completed", workspace.Status);
        Assert.Equal(10002, workspace.Page!.Total);
        Assert.Equal(2000, workspace.Page.Rows.Count);
        await workspace.LoadPageAsync(new(Search: "last marker"));
        Assert.Equal(1, workspace.Page!.Matches);
        Assert.Equal(10002, Assert.Single(workspace.Page.Rows).LineNumber);
        Assert.Equal("last marker", (await workspace.GetRecordAsync(workspace.Page.Rows[0].Id)).Fields[1].Value);
        await workspace.ParseTextAsync("SYN|replacement|1");
        Assert.Equal(1, workspace.Page!.Total);
    }

    private sealed class PasteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task SmallClipboardPasteRemainsEditableAndDoesNotParseAutomatically()
    {
        using var workspace = Workspace(new Store());
        const string text = "SYN| café |7\r\nSYN|two|2";
        var source = new PasteStream(System.Text.Encoding.UTF8.GetBytes(text));
        var editable = await workspace.PreparePasteAsync(_ => Task.FromResult<Stream>(source));
        Assert.Equal(text, editable);
        Assert.True(source.Disposed);
        Assert.False(workspace.HasStagedPaste);
        Assert.Null(workspace.Page);
        await workspace.ParseTextAsync(editable!);
        Assert.Equal(2, workspace.Page!.Total);
    }

    [Fact]
    public async Task LargeClipboardPasteUsesParseTextAndSurvivesFormatRetriesUntilCleared()
    {
        using var workspace = Workspace(new Store());
        var line = "SYN|" + new string('x', 2048) + "|1\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(line, 3000)) + "SYN|end marker|2");
        var source = new PasteStream(bytes);
        Assert.Null(await workspace.PreparePasteAsync(_ => Task.FromResult<Stream>(source)));
        Assert.True(workspace.HasStagedPaste);
        Assert.False(source.Disposed);
        Assert.Contains("Large paste ready", workspace.StagedPasteInfo);
        Assert.Null(workspace.Page);
        workspace.SelectedFormatId = "unavailable";
        await workspace.ParseTextAsync("");
        Assert.Null(workspace.Page);
        Assert.True(workspace.HasStagedPaste);
        workspace.SelectedFormatId = workspace.Parsers[0].FormatId;
        await workspace.ParseTextAsync("");
        Assert.Equal("Completed", workspace.Status);
        Assert.Equal(3001, workspace.Page!.Total);
        Assert.Equal(2000, workspace.Page.Rows.Count);
        await workspace.ParseTextAsync("");
        Assert.Equal(3001, workspace.Page!.Total);
        workspace.ClearStagedPaste();
        Assert.False(workspace.HasStagedPaste);
        Assert.True(source.Disposed);
    }
}


