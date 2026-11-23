using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Text;

public sealed class TextCompareWorkspace : ToolWorkspace
{
    public override string ToolId => "text-compare";
    public override string Title => "Text Compare";
    private string original = "", modified = "", error = "", summary = "";
    private TextComparisonResult? result;
    private int revision;
    public string Original { get => original; set { if (SetProperty(ref original, value)) Invalidate(); } }
    public string Modified { get => modified; set { if (SetProperty(ref modified, value)) Invalidate(); } }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public TextComparisonResult? Result { get => result; private set { if (SetProperty(ref result, value)) OnPropertyChanged(nameof(HasResult)); } }
    public bool HasResult => Result is not null;
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
                Result = null;
                Error = Summary = "";
                try
                {
                    var comparison = await Task.Run(() => TextComparison.Compare(before, after, token), token);
                    token.ThrowIfCancellationRequested();
                    if (version != revision) return;
                    Summary = comparison.AreEqual ? "Texts are identical." : "Differences highlighted: red = removed, green = added.";
                    if (comparison.HasCoarseChanges) Summary += " Large changes are highlighted as whole blocks.";
                    Result = comparison;
                }
                catch (FormatException ex) { if (version == revision) Error = ex.Message; }
            });
            if (Error.Length > 0) Status = "Check input";
        });
        ClearCommand = new RelayCommand(() => { Original = Modified = ""; Invalidate(); });
        SwapCommand = new RelayCommand(() => { var previous = Original; Original = Modified; Modified = previous; });
    }

    private void Invalidate()
    {
        revision++;
        Result = null;
        Error = Summary = "";
        if (!IsBusy) Status = "Ready";
    }

    public override void Dispose() { base.Dispose(); ClearCommand.Execute(null); }
}
