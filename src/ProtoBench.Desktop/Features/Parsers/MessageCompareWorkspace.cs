using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Core.Documents;
using ProtoBench.Parsing;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Registry;

namespace ProtoBench.Desktop.Features.Parsers;

public sealed class MessageCompareWorkspace : ToolWorkspace
{
    private readonly IParserRegistry registry;
    private string message1 = "", message2 = "", summary = "", diagnostics = "";
    private string? selectedFormatId;
    private bool differencesOnly = true;
    private bool showSubfields = true;
    private bool showRequest = true, showReply = true;
    private bool hasReplies;
    private int requestRowCount;
    private string fieldSearch = "";
    private IReadOnlyList<ComparisonFieldFilter> fields = [];
    private int revision;
    private IReadOnlyList<FieldComparison> rows = [];
    public override string ToolId => "message-compare";
    public override string Title => "Message Compare";
    public IReadOnlyList<ILogParser> Parsers => registry.Parsers;
    public string Message1 { get => message1; set { if (SetProperty(ref message1, value)) Invalidate(); } }
    public string Message2 { get => message2; set { if (SetProperty(ref message2, value)) Invalidate(); } }
    public string? SelectedFormatId { get => selectedFormatId; set { if (SetProperty(ref selectedFormatId, value)) Invalidate(); } }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public string Diagnostics { get => diagnostics; private set => SetProperty(ref diagnostics, value); }
    public IReadOnlyList<FieldComparison> Rows
    {
        get => rows;
        private set
        {
            foreach (var option in fields) option.PropertyChanged -= FieldSelectionChanged;
            SetProperty(ref rows, value);
            fields = value.DistinctBy(FieldPath).Select(row => new ComparisonFieldFilter(FieldPath(row), row.Name)).ToArray();
            foreach (var option in fields) option.PropertyChanged += FieldSelectionChanged;
            OnPropertyChanged(nameof(Fields));
            OnPropertyChanged(nameof(VisibleFields));
            RefreshFilters();
        }
    }
    public IEnumerable<FieldComparison> VisibleRows
    {
        get
        {
            var selected = Fields.Where(option => option.IsSelected).Select(option => option.Path).ToHashSet(StringComparer.Ordinal);
            return Rows.Where((row, index) => (index < requestRowCount ? ShowRequest : ShowReply)
                && (ShowSubfields || row.Depth == 0)
                && (!DifferencesOnly || row.Status != FieldComparisonStatus.Match) && selected.Contains(FieldPath(row)));
        }
    }
    public bool ShowRequest { get => showRequest; set { if (SetProperty(ref showRequest, value)) OnPropertyChanged(nameof(VisibleRows)); } }
    public bool ShowReply { get => showReply; set { if (SetProperty(ref showReply, value)) OnPropertyChanged(nameof(VisibleRows)); } }
    public bool ShowSubfields { get => showSubfields; set { if (SetProperty(ref showSubfields, value)) OnPropertyChanged(nameof(VisibleRows)); } }
    public IReadOnlyList<ComparisonFieldFilter> Fields => fields;
    public string FieldSearch { get => fieldSearch; set { if (SetProperty(ref fieldSearch, value)) OnPropertyChanged(nameof(VisibleFields)); } }
    public IEnumerable<ComparisonFieldFilter> VisibleFields
    {
        get
        {
            var terms = FieldSearch.Split([',', ';', '\r', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return Fields.Where(option => terms.Length == 0 || terms.Any(term =>
                option.Path.Contains(term, StringComparison.OrdinalIgnoreCase) || option.Name.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }
    }
    public string FieldFilterLabel => Fields.All(option => option.IsSelected) ? "Fields: All" : $"Fields: {Fields.Count(option => option.IsSelected)} of {Fields.Count}";
    public bool DifferencesOnly { get => differencesOnly; set { if (SetProperty(ref differencesOnly, value)) OnPropertyChanged(nameof(VisibleRows)); } }
    public IAsyncRelayCommand CompareCommand { get; }
    public IRelayCommand ClearCommand { get; }

    public void SelectAllFields(bool selected)
    {
        foreach (var option in Fields) option.IsSelected = selected;
    }

    public void SelectMatchingFields()
    {
        var matching = VisibleFields.ToHashSet();
        foreach (var option in Fields) option.IsSelected = matching.Contains(option);
    }

    private string FieldPath(FieldComparison row) => hasReplies ? row.Path[(row.Path.IndexOf(" / ", StringComparison.Ordinal) + 3)..] : row.Path;
    private void FieldSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshFilters();
    private void RefreshFilters()
    {
        OnPropertyChanged(nameof(VisibleRows));
        OnPropertyChanged(nameof(FieldFilterLabel));
    }

    public MessageCompareWorkspace(IParserRegistry registry)
    {
        this.registry = registry;
        CompareCommand = new AsyncRelayCommand(CompareAsync);
        ClearCommand = new RelayCommand(() => { Message1 = Message2 = ""; Invalidate(); });
    }

    private Task CompareAsync() => RunAsync(async token =>
    {
        Rows = []; Summary = Diagnostics = "";
        if (Parsers.Count == 0) throw new InvalidDataException("No parser specs are installed. Import a parser extension in Settings → Plugins & custom tools, then reload the app.");
        if (SelectedFormatId is null) throw new InvalidDataException("Select a spec before comparing.");
        if (string.IsNullOrWhiteSpace(Message1) || string.IsNullOrWhiteSpace(Message2))
            throw new InvalidDataException("Paste content into Message 1 and Message 2.");
        var parser = registry.Get(SelectedFormatId);
        var version = revision;
        var first = Message1; var second = Message2;
        var result = await Task.Run(async () =>
        {
            async Task<ParseResult> Parse(string text)
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
                return await parser.ParseAsync(stream, token);
            }
            var a = await Parse(first);
            token.ThrowIfCancellationRequested();
            var b = await Parse(second);
            return (a, b);
        }, token);
        token.ThrowIfCancellationRequested();
        if (version != revision) return;
        Diagnostics = string.Join(Environment.NewLine, new[] { result.a, result.b }.SelectMany((p, i) =>
            p.Diagnostics.Select(d => $"Message {i + 1}: {d.Severity} {d.Code}: {d.Message}")));
        Validate(result.a, 1); Validate(result.b, 2);
        if (result.a.Records.Count != result.b.Records.Count)
            throw new InvalidDataException("Message 1 and Message 2 must contain the same number of messages: one request each, or a request followed by its reply in each.");
        var paired = result.a.Records.Count == 2;
        var compared = await Task.Run(() =>
        {
            var comparisons = new List<FieldComparison>();
            var requestCount = 0;
            for (var i = 0; i < result.a.Records.Count; i++)
            {
                var section = i == 0 ? "Request" : "Reply";
                comparisons.AddRange(MessageComparison.Compare(result.a.Records[i], result.b.Records[i], token)
                    .Select(row => paired ? row with { Path = section + " / " + row.Path } : row));
                if (i == 0) requestCount = comparisons.Count;
            }
            return (Rows: comparisons, RequestCount: requestCount);
        }, token);
        token.ThrowIfCancellationRequested();
        if (version != revision) return;
        var differences = compared.Rows.Count(r => r.Status != FieldComparisonStatus.Match);
        Summary = $"{(paired ? "Request + reply" : "Single message")} · {compared.Rows.Count:N0} fields and subfields · {compared.Rows.Count - differences:N0} match · {differences:N0} differ";
        hasReplies = paired;
        requestRowCount = compared.RequestCount;
        Rows = compared.Rows;
    });

    private static void Validate(ParseResult result, int number)
    {
        if (result.IsTruncated) throw new InvalidDataException($"Message {number}: parser returned a partial preview. Complete messages are required.");
        if (result.Records.Count is < 1 or > 2) throw new InvalidDataException($"Message {number}: expected one request or a request followed by its reply (two messages); the spec parsed {result.Records.Count}.");
        for (var i = 0; i < result.Records.Count; i++)
            if (result.Records[i].Fields.Count == 0)
                throw new InvalidDataException($"Message {number}, {(i == 0 ? "request" : "reply")}: the selected spec produced no fields.");
    }

    private void Invalidate()
    {
        revision++;
        Rows = []; Summary = Diagnostics = "";
        if (!IsBusy) Status = "Ready";
    }

    public override void Dispose() { revision++; base.Dispose(); }
}

public sealed class ComparisonFieldFilter(string path, string name) : ObservableObject
{
    private bool isSelected = true;
    public string Path { get; } = path;
    public string Name { get; } = name;
    public string Label => Path == Name ? Path : $"{Path} — {Name}";
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
}
