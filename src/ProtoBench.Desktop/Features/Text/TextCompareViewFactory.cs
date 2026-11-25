using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Text;

public sealed partial class TextCompareViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "text-compare";
    public FrameworkElement Create(ToolWorkspace workspace) => new ComparisonSurface((TextCompareWorkspace)workspace, dialogs).Root;

    // The source stays complete; CodeMirror draws only the viewport and a small buffer.
    private sealed class ComparisonEditor : Grid
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        private readonly Func<string> read;
        private readonly Action<string> write;
        private readonly WebView2 web = new();
        private readonly TextBlock error = Ui.Text("");
        private readonly Queue<object> pending = new();
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<int> starts = [0];
        private readonly Dictionary<int, double> lineTops = [];
        private string current = "";
        private string? publishedText;
        private int documentVersion, revision, selectionStart, selectionEnd;
        private readonly string diagnosticName;
        private bool initializing, loaded, whitespace;
        private double scrollTop, scrollLeft;
        private IReadOnlyList<TextCompareLineSpace> spaces = [];
        public Grid Pane { get; } = new() { RowSpacing = 6 };
        public Func<string, string>? NormalizeEdit { get; set; }
        public int CaretLine { get; private set; } = 1;
        public double LineHeight { get; private set; } = 22;
        public string SelectedText => current[Math.Clamp(selectionStart, 0, current.Length)..Math.Clamp(selectionEnd, Math.Clamp(selectionStart, 0, current.Length), current.Length)];
        public bool ShowWhitespace { get => whitespace; set { whitespace = value; Send(new { type = "whitespace", value }); } }
        public event Action<ComparisonEditor>? ScrollChanged;
        public event Action? ViewportChanged;
        public event Action? GeometryChanged;
        public event EventHandler? SelectionChanged;
        public event Action? FindRequested;
        public event Action<int>? NavigateRequested;
        public event Action<bool>? CopyRequested;
        public event Action<int>? LineClicked;
        public event Action<int, double, double>? LineHighlightGeometryChanged;

        public ComparisonEditor(string title, Func<string> read, Action<string> write)
        {
            this.read = read; this.write = write;
            diagnosticName = title.StartsWith("Original", StringComparison.Ordinal) ? "original" : "modified";
            MinHeight = 0;
            Children.Add(web); Children.Add(error); error.Visibility = Visibility.Collapsed;
            Pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Pane.RowDefinitions.Add(new RowDefinition());
            Pane.Children.Add(Ui.Text(title)); Grid.SetRow(this, 1); Pane.Children.Add(this);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(web, title);
            Loaded += async (_, _) => await InitializeAsync(title);
            ActualThemeChanged += (_, _) => SendTheme();
            SyncText();
        }

        private async Task InitializeAsync(string title)
        {
            if (initializing) return;
            initializing = true;
            try
            {
                var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProtoBench", "TextCompareWebView");
                var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, data, null);
                await web.EnsureCoreWebView2Async(environment);
                var core = web.CoreWebView2;
                core.SetVirtualHostNameToFolderMapping("text-compare.invalid", Path.Combine(AppContext.BaseDirectory, "Assets", "TextCompareEditor"), CoreWebView2HostResourceAccessKind.DenyCors);
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;
                core.NavigationStarting += (_, e) => { if (e.Uri != "https://text-compare.invalid/editor.html") e.Cancel = true; };
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.WebMessageReceived += (_, e) => { if (e.Source == "https://text-compare.invalid/editor.html") Receive(e.WebMessageAsJson); };
                core.NavigationCompleted += (_, e) =>
                {
                    if (e.IsSuccess) return;
                    var failure = new InvalidOperationException($"Editor navigation failed: {e.WebErrorStatus}");
                    error.Text = failure.Message; error.Visibility = Visibility.Visible; ready.TrySetException(failure);
                };
                core.Navigate("https://text-compare.invalid/editor.html");
                pending.Enqueue(new { type = "label", value = title });
            }
            catch (Exception ex)
            {
                error.Text = $"Could not start the text editor: {ex.Message}";
                error.Visibility = Visibility.Visible; ready.TrySetException(ex);
            }
        }
        private void SendTheme() => Send(new { type = "theme", dark = ActualTheme != ElementTheme.Light });
        private void Send(object message)
        {
            if (!loaded) { pending.Enqueue(message); return; }
            web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
        }
        private void Receive(string json)
        {
            using var document = JsonDocument.Parse(json);
            var message = document.RootElement;
            switch (message.GetProperty("type").GetString())
            {
                case "ready":
                    loaded = true;
                    while (pending.TryDequeue(out var next)) Send(next);
                    SendTheme(); ready.TrySetResult(); break;
                case "change":
                    if (message.GetProperty("version").GetInt32() != documentVersion) return;
                    var nextRevision = message.GetProperty("revision").GetInt32();
                    if (nextRevision <= revision) return;
                    foreach (var patch in message.GetProperty("patches").EnumerateArray())
                    {
                        var from = patch.GetProperty("from").GetInt32(); var to = patch.GetProperty("to").GetInt32();
                        current = current.Remove(from, to - from).Insert(from, patch.GetProperty("text").GetString() ?? "");
                    }
                    revision = nextRevision; IndexLines(); Publish(); break;
                case "selection":
                    selectionStart = message.GetProperty("start").GetInt32(); selectionEnd = message.GetProperty("end").GetInt32();
                    CaretLine = message.GetProperty("line").GetInt32(); SelectionChanged?.Invoke(this, EventArgs.Empty); break;
                case "scroll":
                    scrollTop = message.GetProperty("top").GetDouble(); scrollLeft = message.GetProperty("left").GetDouble();
                    ScrollChanged?.Invoke(this);
                    break;
                case "viewport":
                    scrollTop = message.GetProperty("top").GetDouble(); scrollLeft = message.GetProperty("left").GetDouble();
                    LineHeight = message.GetProperty("lineHeight").GetDouble(); lineTops.Clear();
                    foreach (var line in message.GetProperty("lines").EnumerateArray()) lineTops[line[0].GetInt32()] = line[1].GetDouble();
                    ViewportChanged?.Invoke();
#if DEBUG
                    var diagnostics = Environment.GetEnvironmentVariable("PROTOBENCH_TEXT_COMPARE_DIAGNOSTICS");
                    if (!string.IsNullOrEmpty(diagnostics))
                        File.WriteAllText(Path.Combine(diagnostics, diagnosticName + ".json"), JsonSerializer.Serialize(new
                        { lineCount = message.GetProperty("lineCount").GetInt32(), renderedLines = message.GetProperty("renderedLines").GetInt32(), characters = current.Length, scrollTop, revision }));
#endif
                    break;
                case "layout": GeometryChanged?.Invoke(); break;
                case "lineClick":
                    if (message.GetProperty("version").GetInt32() == documentVersion)
                        LineClicked?.Invoke(message.GetProperty("line").GetInt32());
                    break;
                case "lineHighlightGeometry":
                    if (message.GetProperty("version").GetInt32() == documentVersion)
                        LineHighlightGeometryChanged?.Invoke(message.GetProperty("line").GetInt32(), message.GetProperty("top").GetDouble(), message.GetProperty("height").GetDouble());
                    break;
                case "find": FindRequested?.Invoke(); break;
                case "navigate": NavigateRequested?.Invoke(message.GetProperty("direction").GetInt32()); break;
                case "copy": CopyRequested?.Invoke(message.GetProperty("toRight").GetBoolean()); break;
                case "error": error.Text = message.GetProperty("message").GetString(); error.Visibility = Visibility.Visible; break;
            }
        }
        private void Publish()
        {
            publishedText = NormalizeEdit?.Invoke(current) ?? current;
            write(publishedText);
        }
        private void IndexLines()
        {
            starts.Clear(); starts.Add(0);
            for (var i = 0; i < current.Length; i++) if (current[i] == '\n') starts.Add(i + 1);
        }
        public void SyncText()
        {
            var source = read();
            if (ReferenceEquals(source, publishedText)) return;
            var text = source.ReplaceLineEndings("\n");
            if (text == current) return;
            current = text; revision = 0; IndexLines(); Send(new { type = "text", value = current, version = ++documentVersion });
        }
        public async Task FlushAsync()
        {
            await ready.Task;
            var stateJson = await web.CoreWebView2.ExecuteScriptAsync("editorBridge.state()");
            using var state = JsonDocument.Parse(stateJson);
            if (state.RootElement.GetProperty("version").GetInt32() != documentVersion) return;
            selectionStart = state.RootElement.GetProperty("start").GetInt32(); selectionEnd = state.RootElement.GetProperty("end").GetInt32();
            CaretLine = state.RootElement.GetProperty("line").GetInt32();
            if (state.RootElement.GetProperty("revision").GetInt32() == revision) return;
            var json = await web.CoreWebView2.ExecuteScriptAsync("editorBridge.snapshot()");
            using var snapshot = JsonDocument.Parse(json);
            if (snapshot.RootElement.GetProperty("version").GetInt32() != documentVersion) return;
            if (snapshot.RootElement.GetProperty("revision").GetInt32() < revision) return;
            revision = Math.Max(revision, snapshot.RootElement.GetProperty("revision").GetInt32());
            var text = snapshot.RootElement.GetProperty("text").GetString() ?? "";
            if (text == current) return;
            current = text; IndexLines(); Publish();
        }
        public int Position(ComparisonLine? line) => line is null ? current.Length : starts[Math.Clamp(line.Number - 1, 0, starts.Count - 1)];
        public double LineTop(ComparisonLine? line) => line is not null && lineTops.TryGetValue(line.Number, out var top) ? top : -1_000_000;
        public async Task RevealAsync(ComparisonLine? line, int fallbackPosition)
        {
            await ready.Task;
            var target = JsonSerializer.Serialize(new { line = line?.Number, position = line is null ? (int?)fallbackPosition : null }, Json);
            using var result = JsonDocument.Parse(await web.CoreWebView2.ExecuteScriptAsync($"editorBridge.reveal({target})"));
            scrollTop = result.RootElement.GetProperty("top").GetDouble();
            scrollLeft = result.RootElement.GetProperty("left").GetDouble();
        }
        public void MarkSelected(IEnumerable<int> lines) => Send(new { type = "selected", lines = lines.ToArray() });
        public void HighlightLine(int? line, bool enabled) => Send(new { type = "lineHighlight", line, enabled });
        public void HighlightLineGeometry(double top, double height) => Send(new { type = "lineHighlightGeometry", top, height });
        public void SetWrapping(bool enabled) => Send(new { type = "wrap", value = enabled });
        public void SetFontSize(int size) => Send(new { type = "fontSize", value = size });
        public void SyncScrollFrom(ComparisonEditor source)
        {
            if (Math.Abs(source.scrollTop - scrollTop) < .5 && Math.Abs(source.scrollLeft - scrollLeft) < .5) return;
            Send(new { type = "scroll", top = source.scrollTop, left = source.scrollLeft });
        }
        public async Task<bool> FindAsync(string text)
        {
            if (text.Length == 0) return false;
            await FlushAsync();
            var found = current.IndexOf(text, Math.Clamp(selectionEnd, 0, current.Length), StringComparison.OrdinalIgnoreCase);
            if (found < 0) found = current.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            selectionStart = found; selectionEnd = found + text.Length;
            Send(new { type = "select", start = selectionStart, end = selectionEnd });
            return true;
        }
        public void ReplaceSelection(string text) => Send(new { type = "replace", value = text.ReplaceLineEndings("\n") });
        public void ResetSpacing() => SetSpacing([]);
        public void SetSpacing(IReadOnlyList<TextCompareLineSpace> value)
        {
            if (spaces.SequenceEqual(value)) return;
            spaces = value; Send(new { type = "spacing", spaces = value });
        }
        public async Task<double[]> MeasureLinesAsync()
        {
            await ready.Task;
            return JsonSerializer.Deserialize<double[]>(await web.CoreWebView2.ExecuteScriptAsync("editorBridge.measure()")) ?? [];
        }
        public void Highlight(TextComparisonResult? result, TextDifferenceKind kind, TextCompareDocument? alignment = null)
        {
            var weak = new List<(int Start, int End)>(); var strong = new List<(int Start, int End)>();
            if (result is not null)
            {
                if (alignment is not null)
                    foreach (var row in alignment.Rows)
                    {
                        var line = kind == TextDifferenceKind.Removed ? row.Left : row.Right;
                        if (row.IsChange && line is not null && line.Content.Length > 0) weak.Add((Position(line), Position(line) + line.Content.Length));
                    }
                var offset = 0; var index = 0;
                foreach (var difference in result.Differences)
                {
                    if (difference.Kind != TextDifferenceKind.Equal && difference.Kind != kind) continue;
                    var length = difference.Text.ReplaceLineEndings("\n").Length;
                    if (difference.Kind == kind)
                    {
                        if (alignment is null) strong.Add((offset, offset + length));
                        else
                        {
                            while (index < weak.Count && weak[index].End <= offset) index++;
                            for (var i = index; i < weak.Count && weak[i].Start < offset + length; i++)
                                strong.Add((Math.Max(offset, weak[i].Start), Math.Min(offset + length, weak[i].End)));
                        }
                    }
                    offset += length;
                }
            }
            Send(new { type = "highlights", removed = kind == TextDifferenceKind.Removed,
                weak = weak.Select(r => new[] { r.Start, r.End }).ToArray(), strong = strong.Select(r => new[] { r.Start, r.End }).ToArray() });
        }
    }
}
