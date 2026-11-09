using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Core.Documents;
using NonStopToolkit.Infrastructure.Logs;
using NonStopToolkit.Parsing.Registry;
using NonStopToolkit.Parsing.Engines;
using NonStopToolkit.Desktop.Services;
using Microsoft.Extensions.Logging;
namespace NonStopToolkit.Desktop.Features.Parsers;

public sealed class ParserWorkspace : ToolWorkspace
{
    private readonly IParserRegistry registry;
    private readonly SessionSettings settings;
    private readonly ILogger<ParserWorkspace> logger;
    public ParserWorkspace(IParserRegistry registry, SessionSettings settings, ILogger<ParserWorkspace> logger)
    {
        this.registry = registry;
        this.settings = settings;
        this.logger = logger;
        settings.Changed += SettingsChanged;
    }
    private void SettingsChanged()
    {
        OnPropertyChanged(nameof(AutoFitResults));
        OnPropertyChanged(nameof(VerboseChecks));
    }
    public override string ToolId => "log-parser";
    public override string Title => "Log Parser";
    public IReadOnlyList<NonStopToolkit.Parsing.Abstractions.ILogParser> Parsers => registry.Parsers;
    public string? SelectedFormatId { get; set; }
    private string path = "", diagnostics = "", candidates = "";
    private LogPage? page;
    private LogStore? store;
    private Stream? stagedPaste;
    private string stagedPasteInfo = "";
    public const int EditablePasteBytes = 4 * 1024 * 1024;
    private CancellationTokenSource? queryCancellation;
    private readonly CancellationTokenSource lifetime = new();
    private long queryVersion;
    private bool disposed, querying;
    public string Path { get => path; private set => SetProperty(ref path, value); }
    public string Diagnostics { get => diagnostics; private set => SetProperty(ref diagnostics, value); }
    public string DiagnosticSummary => store is null ? "Parsing diagnostics" :
        $"Parsing diagnostics · {store.DiagnosticErrors:N0} errors · {store.DiagnosticWarnings:N0} warnings"
        + (store.DiagnosticInformation > 0 ? $" · {store.DiagnosticInformation:N0} notices" : "")
        + (store.IsTruncated ? " · partial preview" : "");
    public string Candidates { get => candidates; private set => SetProperty(ref candidates, value); }
    public LogPage? Page { get => page; private set => SetProperty(ref page, value); }
    public bool IsQuerying { get => querying; private set => SetProperty(ref querying, value); }
    public bool IsTruncated => store?.IsTruncated == true;
    public LogQuery Query { get; private set; } = new();
    public bool AutoScrollResults => settings.Current.AutoScrollParserResults;
    public bool AutoFitResults => settings.Current.AutoFitParserResults;
    public bool VerboseChecks => settings.Current.VerboseParserChecks;
    public IReadOnlyList<FieldCheck> GetCheckDetails(ParsedRecord record) =>
        Parsers.OfType<RecordMessageParser>().FirstOrDefault(p => p.FormatId == record.Message?.ProfileId)
            ?.GetCheckDetails(record) ?? [];
    public async Task SetAutoFitResultsAsync(bool enabled)
    {
        await settings.SetParserAutoFitAsync(enabled);
        OnPropertyChanged(nameof(AutoFitResults));
    }
    public bool HasStagedPaste => stagedPaste is not null;
    public string StagedPasteInfo { get => stagedPasteInfo; private set => SetProperty(ref stagedPasteInfo, value); }
    public Task OpenAsync(string file)
    {
        if (!IsBusy) ClearStagedPaste();
        return ParseInputAsync(file, null, null, file);
    }
    public Task ParseTextAsync(string text)
    {
        if (stagedPaste is not { } input) return ParseInputAsync(null, text, null, "Pasted text (UTF-8)");
        return ParseInputAsync(null, null, _ =>
        {
            input.Position = 0;
            return Task.FromResult<Stream>(new BorrowedInput(input));
        }, "Pasted text (large)");
    }
    public Task ParseStreamAsync(Func<CancellationToken, Task<Stream>> source, string label) => ParseInputAsync(null, null, source, label);
    public void CancelSearch() { var previous = queryCancellation; queryCancellation = null; previous?.Cancel(); }
    public void CancelWork() { Cancel(); CancelSearch(); }

    /// <summary>Small clipboard contents remain editable; large ones are staged until Parse text is chosen.</summary>
    public async Task<string?> PreparePasteAsync(Func<CancellationToken, Task<Stream>> source)
    {
        string? editable = null;
        await RunAsync(async token =>
        {
            Stream? input = null;
            try
            {
                input = await source(token);
                token.ThrowIfCancellationRequested();
                if (disposed) throw new OperationCanceledException(token);
                if (!input.CanSeek) throw new InvalidDataException("Pasted input must support seeking.");
                if (input.Length <= EditablePasteBytes)
                {
                    using var reader = new StreamReader(input, new System.Text.UTF8Encoding(false, true), true, 65536, leaveOpen: true);
                    editable = await reader.ReadToEndAsync(token);
                    ClearStagedPaste();
                }
                else
                {
                    ClearStagedPaste();
                    stagedPaste = input; input = null;
                    StagedPasteInfo = $"Large paste ready · {stagedPaste.Length / (1024d * 1024):N1} MiB. Choose Parse text to continue.";
                    OnPropertyChanged(nameof(HasStagedPaste));
                }
            }
            finally { if (input is not null) await input.DisposeAsync(); }
        });
        return editable;
    }

    public void ClearStagedPaste()
    {
        stagedPaste?.Dispose(); stagedPaste = null;
        StagedPasteInfo = "";
        OnPropertyChanged(nameof(HasStagedPaste));
    }

    public async Task LoadPageAsync(LogQuery query)
    {
        CancelSearch();
        var version = ++queryVersion;
        var index = store;
        if (index is null || disposed) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        queryCancellation = cancellation;
        IsQuerying = true;
        try
        {
            var result = await index.QueryAsync(query, cancellation.Token);
            if (version != queryVersion || !ReferenceEquals(store, index) || disposed) return;
            Query = query with { PageIndex = result.PageIndex };
            Page = result;
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (disposed || !ReferenceEquals(store, index)) { }
        catch (Exception ex) { Status = ex.Message; }
        finally
        {
            if (ReferenceEquals(queryCancellation, cancellation)) queryCancellation = null;
            if (version == queryVersion) IsQuerying = false;
        }
    }

    public Task<ParsedRecord> GetRecordAsync(long id, CancellationToken token = default) =>
        (store ?? throw new InvalidOperationException("Open a log first.")).ReadAsync(id, token);
    public IReadOnlyList<LogTableColumn> TableColumns => store?.TableColumns ?? [];
    public bool TableColumnsLimited => store?.TableColumnsLimited == true;
    public Task<LogTablePage> GetTablePageAsync(IReadOnlyList<LogRecordSummary> rows, IReadOnlyList<string> columns, CancellationToken token = default) =>
        (store ?? throw new InvalidOperationException("Open a log first.")).ReadTableAsync(rows, columns, token);
    public Task<IReadOnlyList<LogRecordSummary>> GetRelatedAsync(long id, ParsedRecord selected, CancellationToken token = default)
    {
        var parser = Parsers.OfType<RecordMessageParser>().FirstOrDefault(p => p.FormatId == selected.Message?.ProfileId);
        return store is not null ? store.RelatedAsync(id, selected, parser, token)
            : Task.FromResult<IReadOnlyList<LogRecordSummary>>([]);
    }

    public string? RelatedUnavailableReason(ParsedRecord record)
    {
        if (record.Message is null) return "This parser did not provide message metadata for related-message lookup.";
        if (MessageAnalysis.LoggedTime(record) is null)
            return "Related-message lookup needs a known message date and time.";
        var correlation = record.Correlation ?? Parsers.OfType<RecordMessageParser>()
            .FirstOrDefault(p => p.FormatId == record.Message.ProfileId)?.GetCorrelation(record.Message.MessageType);
        if (correlation is null || !correlation.IsValid())
            return "This parser did not provide related-message rules for this message, or the supplied rules are invalid.";
        var missingKeys = correlation.Keys.Where(k => MessageAnalysis.Value(record, k).Length == 0).ToArray();
        return missingKeys.Length > 0 ? "Related-message lookup needs the configured fields: " + string.Join(", ", missingKeys) + "."
            : correlation.MessageTypes is not { Count: > 0 } ? "No related message types are configured for this message." : null;
    }

    private Task ParseInputAsync(string? file, string? text, Func<CancellationToken, Task<Stream>>? source, string label) => RunAsync(async token =>
    {
        CancelSearch();
        ++queryVersion;
        IsQuerying = false;
        store?.Dispose(); store = null;
        Page = null;
        Diagnostics = Candidates = "";
        Path = label;
        Query = new LogQuery();
        var selected = SelectedFormatId;
        var progress = new Progress<LogImportProgress>(value =>
        {
            if (!disposed && IsBusy && !token.IsCancellationRequested)
                Status = $"{value.Stage} · {value.BytesRead / (1024d * 1024):N1} MiB · {value.Records:N0} messages";
        });
        LogStore? pending = null;
        try
        {
            var imported = await Task.Run(async () =>
            {
                token.ThrowIfCancellationRequested();
                if (registry.Parsers.Count == 0)
                    throw new InvalidDataException("No parser specs are installed. Import a parser extension in Settings → Plugins & custom tools, then reload the app.");
                if (file is null && source is null && string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException("Paste log text before parsing.");
                await using Stream stream = file is not null
                    ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan)
                    : source is not null ? await source(token) : await SpoolTextAsync(text!, token);
                if (!stream.CanSeek) throw new InvalidDataException("The input source must support seeking.");
                var sample = new byte[4096];
                var count = await stream.ReadAtLeastAsync(sample, sample.Length, throwOnEndOfStream: false, cancellationToken: token);
                stream.Position = 0;
                var matches = registry.Detect(sample.AsSpan(0, count));
                var info = string.Join(", ", matches.Select(p => p.Name));
                var parser = selected is not null ? registry.Get(selected) : matches.Count == 1 ? matches[0] : throw new InvalidDataException(matches.Count == 0
                    ? "No format detected. Select a parser manually." : $"Multiple formats detected ({info}). Select a parser manually.");
                pending = new LogStore(progress);
                await pending.ImportAsync(parser, stream, token);
                return info;
            }, token);
            token.ThrowIfCancellationRequested();
            if (disposed) throw new OperationCanceledException(token);
            store = pending; pending = null;
            Candidates = "Detection candidates: " + (imported.Length == 0 ? "none (manual selection)" : imported);
            Diagnostics = string.Join(Environment.NewLine, store!.Diagnostics.Select(d => $"{d.Severity} {d.Code} · line {d.Line}: {d.Message}"));
            if (store.IsTruncated) Diagnostics += "\nPlugin returned a partial preview; search covers indexed messages only.";
            await LoadPageAsync(Query);
            if (file is not null) await settings.AddRecentAsync(file);
        }
        catch (Exception) when (token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(new EventId(201), ex, "Parser workspace failed"); throw; }
        finally { pending?.Dispose(); }
    });

    private static async Task<Stream> SpoolTextAsync(string text, CancellationToken token)
    {
        var inputPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nonstop-paste-" + Guid.NewGuid().ToString("N") + ".tmp");
        var stream = new FileStream(inputPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false, true), 65536, leaveOpen: true))
            {
                for (int start = 0; start < text.Length; start += Math.Min(65536, text.Length - start))
                    await writer.WriteAsync(text.AsMemory(start, Math.Min(65536, text.Length - start)), token);
                await writer.FlushAsync(token);
            }
            stream.Position = 0;
            return stream;
        }
        catch { await stream.DisposeAsync(); throw; }
    }
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        settings.Changed -= SettingsChanged;
        lifetime.Cancel(); CancelSearch();
        base.Dispose();
        store?.Dispose(); store = null;
        ClearStagedPaste();
    }

    // The workspace owns staged input so a format-selection error can be retried without another paste.
    private sealed class BorrowedInput(Stream input) : Stream
    {
        public override bool CanRead => input.CanRead;
        public override bool CanSeek => input.CanSeek;
        public override bool CanWrite => false;
        public override long Length => input.Length;
        public override long Position { get => input.Position; set => input.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => input.ReadAsync(buffer, cancellationToken);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => input.ReadAsync(buffer, offset, count, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => input.Seek(offset, origin);
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
