using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Utilities.Text;
using ProtoBench.Desktop.Services;

namespace ProtoBench.Desktop.Features.Text;

public sealed class TextCompareWorkspace : ToolWorkspace
{
    public override string ToolId => "text-compare";
    public override string Title => "Text Compare";
    private string original = "", modified = "", error = "", summary = "";
    private TextComparisonResult? result;
    private int revision;
    private TextCompareDocument? document;
    private TextCompareOptions options = new();
    private int selectedChange = -1;
    private (string Left, string Right)? copyUndo;
    private bool restoring;
    public TextCompareOptions Options { get => options; set { if (SetProperty(ref options, value)) Invalidate(false); } }
    public TextCompareDocument? Alignment { get => document; private set => SetProperty(ref document, value); }
    public int SelectedChange { get => selectedChange; set { if (SetProperty(ref selectedChange, value)) OnPropertyChanged(nameof(Navigation)); } }
    public string Navigation => Alignment?.Changes.Count > 0 ? $"Change {SelectedChange + 1} of {Alignment.Changes.Count}" : "No changes";
    public bool CanUndoCopy => copyUndo is not null;
    public string Original { get => original; set { if (SetProperty(ref original, value)) Invalidate(); } }
    public string Modified { get => modified; set { if (SetProperty(ref modified, value)) Invalidate(); } }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public TextComparisonResult? Result { get => result; private set { if (SetProperty(ref result, value)) OnPropertyChanged(nameof(HasResult)); } }
    public bool HasResult => Result is not null;
    public string CreateDifferencesReport() => ComparisonReport.Text(Summary,
        Result ?? throw new InvalidOperationException("Compare the texts before exporting a report."), Alignment, Options);
    public IAsyncRelayCommand CompareCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand SwapCommand { get; }

    public TextCompareWorkspace()
    {
        CompareCommand = new AsyncRelayCommand(async () =>
        {
            await RunAsync(async token =>
            {
                var version = revision;
                var before = Original;
                var after = Modified;
                var rules = Options;
                Result = null;
                Error = Summary = "";
                try
                {
                    var comparison = await Task.Run(() => TextComparison.Compare(before, after, token), token);
                    var alignment = await Task.Run(() => TextCompareDocument.Create(before, after, rules, token), token);
                    token.ThrowIfCancellationRequested();
                    if (version != revision) return;
                    Alignment = alignment;
                    SelectedChange = alignment.Changes.Count > 0 ? Math.Clamp(SelectedChange, 0, alignment.Changes.Count - 1) : -1;
                    OnPropertyChanged(nameof(Navigation));
                    Summary = alignment.IsExactMatch ? "Texts are identical." : alignment.Changes.Count == 0 ? "Equal under current rules." :
                        $"{Count(alignment.Changes.Count, "changed section")} · {Count(alignment.Rows.Count(r => r.IsChange && r.Left is null), "added line")} · {Count(alignment.Rows.Count(r => r.IsChange && r.Right is null), "removed line")}";
                    if (alignment.Changes.Count > 0 && (comparison.HasCoarseChanges || alignment.IsCoarse)) Summary += " Large changes are highlighted as whole blocks.";
                    Result = comparison;
                }
                catch (FormatException ex) { if (version == revision) Error = ex.Message; }
            });
            if (Error.Length > 0) Status = "Check input";
        });
        ClearCommand = new RelayCommand(() => { Original = Modified = ""; Invalidate(); });
        SwapCommand = new RelayCommand(() => { var previous = Original; Original = Modified; Modified = previous; });
    }

    private static string Count(int value, string label) => $"{value} {label}{(value == 1 ? "" : "s")}";

    private void Invalidate(bool clearCopyUndo = true)
    {
        revision++;
        Cancel();
        Alignment = null;
        if (clearCopyUndo && !restoring && copyUndo is not null) { copyUndo = null; OnPropertyChanged(nameof(CanUndoCopy)); }
        Result = null;
        Error = Summary = "";
        if (!IsBusy) Status = "Ready";
    }

    public void Navigate(int direction)
    {
        if (Alignment is not { Changes.Count: > 0 } aligned) return;
        SelectedChange = (SelectedChange + direction + aligned.Changes.Count) % aligned.Changes.Count;
    }

    public void CopyChange(bool toRight)
    {
        if (Alignment is not { } aligned || SelectedChange < 0 || SelectedChange >= aligned.Changes.Count) return;
        var change = aligned.Changes[SelectedChange];
        var previous = (Original, Modified);
        restoring = true;
        try
        {
            if (toRight) Modified = Modified.Remove(change.RightStart, change.RightLength).Insert(change.RightStart, Original.Substring(change.LeftStart, change.LeftLength));
            else Original = Original.Remove(change.LeftStart, change.LeftLength).Insert(change.LeftStart, Modified.Substring(change.RightStart, change.RightLength));
            copyUndo = previous;
            OnPropertyChanged(nameof(CanUndoCopy));
        }
        finally { restoring = false; }
    }

    public void UndoCopy()
    {
        if (copyUndo is not { } previous) return;
        restoring = true;
        try { Original = previous.Left; Modified = previous.Right; copyUndo = null; OnPropertyChanged(nameof(CanUndoCopy)); }
        finally { restoring = false; }
    }

    public override void Dispose() { base.Dispose(); ClearCommand.Execute(null); }
}
