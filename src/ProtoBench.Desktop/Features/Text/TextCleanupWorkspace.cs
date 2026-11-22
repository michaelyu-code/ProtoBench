using CommunityToolkit.Mvvm.Input;
using ProtoBench.Application.Workspaces;
using ProtoBench.Utilities.Text;
using System.Runtime.CompilerServices;

namespace ProtoBench.Desktop.Features.Text;

public sealed class TextCleanupWorkspace : ToolWorkspace
{
    public override string ToolId => "text-cleanup";
    public override string Title => "Text Cleanup";
    private string input = "", output = "", error = "", summary = "", customCharacters = "";
    private bool spaces = true, tabs, lineEndings, allWhitespace, digits, punctuation, controlCharacters, hasResult;
    private int revision;
    public string Input { get => input; set => Change(ref input, value); }
    public string CustomCharacters { get => customCharacters; set => Change(ref customCharacters, value); }
    public bool Spaces { get => spaces; set => Change(ref spaces, value); }
    public bool Tabs { get => tabs; set => Change(ref tabs, value); }
    public bool LineEndings { get => lineEndings; set => Change(ref lineEndings, value); }
    public bool AllWhitespace { get => allWhitespace; set => Change(ref allWhitespace, value); }
    public bool Digits { get => digits; set => Change(ref digits, value); }
    public bool Punctuation { get => punctuation; set => Change(ref punctuation, value); }
    public bool ControlCharacters { get => controlCharacters; set => Change(ref controlCharacters, value); }
    public string Output { get => output; private set => SetProperty(ref output, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public bool HasResult { get => hasResult; private set => SetProperty(ref hasResult, value); }
    public IAsyncRelayCommand CleanCommand { get; }
    public IRelayCommand ClearCommand { get; }

    private void Change<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (!SetProperty(ref field, value, property)) return;
        revision++;
        Output = Error = Summary = "";
        HasResult = false;
        if (!IsBusy) Status = "Ready";
    }

    public TextCleanupWorkspace()
    {
        CleanCommand = new AsyncRelayCommand(async () =>
        {
            await RunAsync(async token =>
            {
                var version = revision;
                var text = Input;
                var options = new TextCleanupOptions { Spaces = Spaces, Tabs = Tabs, LineEndings = LineEndings,
                    AllWhitespace = AllWhitespace, Digits = Digits, Punctuation = Punctuation,
                    ControlCharacters = ControlCharacters, CustomCharacters = CustomCharacters };
                Output = Error = Summary = "";
                HasResult = false;
                try
                {
                    var result = await Task.Run(() => TextCleanup.Clean(text, options, token), token);
                    token.ThrowIfCancellationRequested();
                    if (version != revision) return;
                    Output = result.Text;
                    Summary = $"Removed {result.RemovedCharacters:N0} character(s).";
                    HasResult = true;
                }
                catch (FormatException ex) { if (version == revision) Error = ex.Message; }
            });
            if (Error.Length > 0) Status = "Check input";
        });
        ClearCommand = new RelayCommand(() => { Input = ""; revision++; Output = Error = Summary = ""; HasResult = false; Status = "Ready"; });
    }

    public override void Dispose() { base.Dispose(); ClearCommand.Execute(null); }
}
