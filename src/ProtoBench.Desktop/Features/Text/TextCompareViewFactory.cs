using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Text;

public sealed partial class TextCompareViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "text-compare";

    public FrameworkElement Create(ToolWorkspace workspace) => new ComparisonSurface((TextCompareWorkspace)workspace, dialogs).Root;

    private sealed class ComparisonEditor : RichEditBox
    {
        private readonly Func<string> read;
        private readonly Action<string> write;
        private readonly ITextCharacterFormat normal;
        private bool updating;
        private int highlightVersion;
        private bool hasSpacing;
        private double tailSpace;
        private ITextRange? highlighted;
        private List<(int Start, int End, bool Strong)> highlights = [];
        private TextDifferenceKind highlightKind;
        private (int Start, int End, int Version) paintedViewport = (-1, -1, -1);
        public bool IsDecorating => updating;
        private readonly LinkedList<(string Text, int Caret)> undo = [], redo = [];
        private bool restoringHistory;
        private long lastEdit;
        private string current = "";
        private string? publishedText;
        private readonly Canvas numbers = new() { IsHitTestVisible = false, Width = 44 };
        private readonly Canvas whitespace = new() { IsHitTestVisible = false };
        private HashSet<int> selectedLines = [];
        public bool ShowWhitespace { get; set; }
        private readonly List<int> lineStarts = [0];
        private ScrollViewer? viewport;
        private bool syncingScroll;
        public event Action<ComparisonEditor>? ScrollChanged;
        private bool numbersPending;
        private double NumberFontSize => normal.Size * 96d / 72d;
        public Grid Pane { get; } = new() { RowSpacing = 6 };
        public Func<string, string>? NormalizeEdit { get; set; }
        public double LineHeight
        {
            get
            {
                Document.GetRange(0, 0).GetRect(PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var rect, out _);
                return rect.Height > 0 ? rect.Height : NumberFontSize * 1.2;
            }
        }
        public int Position(ComparisonLine? line) => line is null ? current.Length : lineStarts[Math.Min(line.Number - 1, lineStarts.Count - 1)];
        public int CaretLine
        {
            get
            {
                var index = lineStarts.BinarySearch(Document.Selection.StartPosition);
                return index >= 0 ? index + 1 : ~index;
            }
        }
        public double TextTop(int position)
        {
            Document.GetRange(position, position).GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Top,
                PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var point);
            return point.Y - (viewport?.VerticalOffset ?? 0);
        }
        public double LineTop(ComparisonLine? line) => TextTop(Position(line));
        public void ResetSpacing()
        {
            if (!hasSpacing && tailSpace == 0) return;
            updating = true;
            Document.BatchDisplayUpdates();
            try
            {
                var format = Document.GetRange(0, int.MaxValue).ParagraphFormat;
                format.SpaceBefore = format.SpaceAfter = 0;
                hasSpacing = false; tailSpace = 0;
            }
            finally { Document.ApplyDisplayUpdates(); updating = false; }
        }
        public void AddSpace(int position, double pixels)
        {
            if (pixels <= .5) return;
            updating = true;
            try { Document.GetRange(position, position).ParagraphFormat.SpaceBefore = (float)(pixels * .75); hasSpacing = true; }
            finally { updating = false; }
        }

        // Task.Yield resumes at normal priority and can starve input during long layout passes.
        public Task YieldToInput()
        {
            var completion = new TaskCompletionSource();
            if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => completion.SetResult()))
                completion.SetResult();
            return completion.Task;
        }
        public void SetTailSpace(double pixels)
        {
            pixels = Math.Max(0, pixels);
            if (Math.Abs(tailSpace - pixels) <= .5) return;
            // Padding reduces RichEdit's text viewport. Large bottom padding can hide
            // every glyph; paragraph spacing extends the document instead.
            updating = true;
            try
            {
                Document.GetRange(current.Length, current.Length).ParagraphFormat.SpaceAfter = (float)(pixels * .75);
                tailSpace = pixels;
            }
            finally { updating = false; }
        }
        public void MarkSelected(IEnumerable<int> lines) { selectedLines = lines.ToHashSet(); ScheduleNumbers(); }
        public void Reveal(ComparisonLine? line)
        {
            if (viewport is null) return;
            viewport.ChangeView(null, Math.Clamp(viewport.VerticalOffset + LineTop(line) - LineHeight * 2, 0, viewport.ScrollableHeight), null, true);
            ScheduleNumbers();
        }
        public bool Find(string text, bool backwards = false)
        {
            if (text.Length == 0) return false;
            var from = backwards ? Math.Max(0, Document.Selection.StartPosition - 1) : Document.Selection.EndPosition;
            var found = backwards ? current.LastIndexOf(text, Math.Min(from, Math.Max(0, current.Length - 1)), StringComparison.OrdinalIgnoreCase)
                : current.IndexOf(text, Math.Min(from, current.Length), StringComparison.OrdinalIgnoreCase);
            if (found < 0) found = current.IndexOf(text, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            Document.Selection.SetRange(found, found + text.Length);
            Document.Selection.ScrollIntoView(PointOptions.None);
            return true;
        }
        public void ReplaceSelection(string replacement) => Document.Selection.SetText(TextSetOptions.None, replacement.ReplaceLineEndings("\r"));
        public string SelectedText { get { Document.Selection.GetText(TextGetOptions.None, out var text); return text; } }

        public ComparisonEditor(string title, Func<string> read, Action<string> write)
        {
            this.read = read;
            this.write = write;
            AcceptsReturn = true;
            MaxLength = 0;
            MinHeight = 0;
            TextWrapping = TextWrapping.NoWrap;
            FontFamily = new FontFamily("Consolas");
            VerticalAlignment = VerticalAlignment.Stretch;
            ScrollViewer.SetVerticalScrollBarVisibility(this, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(this, ScrollBarVisibility.Auto);
            ScrollViewer.SetVerticalScrollMode(this, ScrollMode.Auto);
            ScrollViewer.SetHorizontalScrollMode(this, ScrollMode.Auto);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, title);
            normal = Document.GetDefaultCharacterFormat().GetClone();
            normal.Name = "Consolas";
            normal.Size = (float)FontSize;
            Document.SetDefaultCharacterFormat(normal);
            // Text-only history below owns undo. Native undo would also retain every
            // highlighting and paragraph-spacing operation on large comparisons.
            Document.UndoLimit = 0;
            Pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Pane.Children.Add(Ui.Text(title));
            var body = new Grid();
            body.Children.Add(this);
            body.Children.Add(whitespace);
            numbers.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(numbers);
            Grid.SetRow(body, 1); Pane.Children.Add(body);
            Loaded += (_, _) =>
            {
                viewport = FindViewport(this);
                if (viewport is not null) viewport.ViewChanged += ViewChanged;
                UpdateLineStarts();
            };
            Unloaded += (_, _) =>
            {
                highlightVersion++;
                if (viewport is not null) viewport.ViewChanged -= ViewChanged;
                viewport = null;
                syncingScroll = false;
            };
            SizeChanged += (_, _) => ScheduleNumbers();
            SelectionChanged += (_, _) => { if (!updating) ScheduleNumbers(); };
            PointerWheelChanged += (_, _) => ScheduleNumbers();
            SyncText();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is not (Windows.System.VirtualKey.Z or Windows.System.VirtualKey.Y)) return;
                var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control);
                if (!control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;
                var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                RestoreHistory(e.Key == Windows.System.VirtualKey.Y || shift);
                e.Handled = true;
            };
            TextChanged += (_, _) =>
            {
                if (updating) return;
                Document.GetText(TextGetOptions.None, out var text);
                // RichEdit always includes one final, implicit paragraph mark.
                if (text.EndsWith('\r')) text = text[..^1];
                if (text == current) return;
                highlightVersion++;
                RecordUndo(text, false);
                current = text;
                UpdateLineStarts();
                PublishText(text);
            };
        }

        public void SyncText()
        {
            var source = read();
            // A local edit is already indexed and normalized for RichEdit. Avoid another
            // full-string newline conversion when the workspace echoes it back.
            if (ReferenceEquals(source, publishedText)) return;
            var text = source.ReplaceLineEndings("\r");
            if (text == current) return;
            RecordUndo(text, true);
            highlightVersion++;
            ResetSpacing();
            updating = true;
            try
            {
                Document.SetText(TextSetOptions.None, text);
                Document.GetRange(0, int.MaxValue).CharacterFormat = normal;
                highlighted = null; hasSpacing = false; tailSpace = 0;
                current = text; UpdateLineStarts();
            }
            finally { updating = false; }
        }

        private void PublishText(string text)
        {
            publishedText = NormalizeEdit?.Invoke(text) ?? text;
            write(publishedText);
        }

        // Native RichEdit undo includes decoration changes. Keep a bounded text-only history
        // so Ctrl+Z always undoes an edit rather than highlighting or spacer formatting.
        private void RecordUndo(string next, bool separate)
        {
            if (restoringHistory) return;
            var now = Environment.TickCount64;
            if (separate || now - lastEdit > 750 || Math.Abs(next.Length - current.Length) != 1 || undo.Count == 0)
            {
                undo.AddLast((current, Math.Min(Document.Selection.StartPosition, current.Length)));
                Trim(undo);
            }
            redo.Clear(); lastEdit = now;
        }
        private void RestoreHistory(bool forward)
        {
            var source = forward ? redo : undo; var target = forward ? undo : redo;
            if (source.Last is not { } last) return;
            var snapshot = last.Value; source.RemoveLast();
            highlightVersion++;
            target.AddLast((current, Document.Selection.StartPosition)); Trim(target);
            ResetSpacing();
            updating = restoringHistory = true;
            try
            {
                Document.SetText(TextSetOptions.None, snapshot.Text);
                Document.GetRange(0, int.MaxValue).CharacterFormat = normal;
                highlighted = null;
                current = snapshot.Text;
                UpdateLineStarts();
                var caret = Math.Clamp(snapshot.Caret, 0, current.Length);
                Document.Selection.SetRange(caret, caret);
                PublishText(current);
            }
            finally { updating = restoringHistory = false; lastEdit = 0; }
        }
        private static void Trim(LinkedList<(string Text, int Caret)> history)
        {
            long characters = history.Sum(item => (long)item.Text.Length);
            while (history.Count > 1 && (history.Count > 32 || characters > 8_000_000))
            { characters -= history.First!.Value.Text.Length; history.RemoveFirst(); }
        }

        public void SetWrapping(bool enabled)
        {
            TextWrapping = enabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ScrollViewer.SetHorizontalScrollBarVisibility(this, enabled ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollMode(this, enabled ? ScrollMode.Disabled : ScrollMode.Auto);
            ScheduleNumbers();
        }

        public void SyncScrollFrom(ComparisonEditor source)
        {
            if (viewport is null || source.viewport is null) return;
            var vertical = Math.Clamp(source.viewport.VerticalOffset, 0, viewport.ScrollableHeight);
            var horizontal = Math.Clamp(source.viewport.HorizontalOffset, 0, viewport.ScrollableWidth);
            if (Math.Abs(viewport.VerticalOffset - vertical) < .1 && Math.Abs(viewport.HorizontalOffset - horizontal) < .1) return;
            // Keep the follower's asynchronous ViewChanged events from driving the source
            // back to its position, especially when one text reaches its end first.
            syncingScroll = true;
            if (!viewport.ChangeView(horizontal, vertical, null, disableAnimation: true)) syncingScroll = false;
        }

        private void ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (!syncingScroll && !updating) ScrollChanged?.Invoke(this);
            if (!e.IsIntermediate) syncingScroll = false;
            ScheduleNumbers();
        }

        private static ScrollViewer? FindViewport(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scroll) return scroll;
                if (FindViewport(child) is { } nested) return nested;
            }
            return null;
        }

        private void UpdateLineStarts()
        {
            lineStarts.Clear();
            lineStarts.Add(0);
            for (var i = 0; i < current.Length; i++)
                if (current[i] == '\r') lineStarts.Add(i + 1);
            numbers.Width = Math.Max(44, lineStarts.Count.ToString().Length * NumberFontSize * .65 + 16);
            if (Math.Abs(Padding.Left - numbers.Width - 10) > .1)
                Padding = new Thickness(numbers.Width + 10, Padding.Top, Padding.Right, Padding.Bottom);
            ScheduleNumbers();
        }

        private void ScheduleNumbers()
        {
            if (numbersPending) return;
            numbersPending = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                numbersPending = false;
                DrawNumbers();
            });
        }

        private void DrawNumbers()
        {
            if (!IsLoaded || Pane.Visibility != Visibility.Visible || ActualHeight <= 0) return;
            numbers.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, numbers.ActualWidth, ActualHeight) };
            numbers.Children.Clear();
            whitespace.Children.Clear();
            whitespace.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight) };
            // RichEdit's client coordinates start inside the ScrollViewer's padding.
            var contentTop = viewport is null ? 0 : viewport.TransformToVisual(numbers)
                .TransformPoint(new Windows.Foundation.Point(0, viewport.Padding.Top)).Y;
            double Baseline(int position)
            {
                Document.GetRange(position, position).GetPoint(HorizontalCharacterAlignment.Left,
                    VerticalCharacterAlignment.Baseline, PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var point);
                return contentTop + point.Y - (viewport?.VerticalOffset ?? 0);
            }
            // Hit-test the visible surface instead of asking RichEdit to lay out far-off
            // character positions (especially costly in long or wrapped documents).
            var visualLine = Document.GetRangeFromPoint(new Windows.Foundation.Point(viewport?.HorizontalOffset ?? 0,
                viewport?.VerticalOffset ?? 0), PointOptions.ClientCoordinates);
            visualLine.StartOf(TextRangeUnit.Line, false);
            var low = visualLine.StartPosition;
            // A native layout query may return unusable offscreen geometry while layout is pending.
            // Never walk an entire document to draw a viewport-sized gutter.
            var remaining = Math.Max(8, (int)Math.Ceiling(ActualHeight / Math.Max(1, NumberFontSize)) * 3);
            while (visualLine.StartPosition <= current.Length && remaining-- > 0)
            {
                var position = visualLine.StartPosition;
                var baseline = Baseline(position);
                if (baseline - NumberFontSize >= ActualHeight) break;
                var logicalLine = lineStarts.BinarySearch(position);
                // Only the first visual row gets a number; subsequent rows get a dot.
                var marker = logicalLine >= 0 ? (logicalLine + 1).ToString() : ".";
                var label = new TextBlock
                {
                    Text = marker, FontFamily = FontFamily, FontSize = NumberFontSize,
                    Opacity = logicalLine >= 0 && selectedLines.Contains(logicalLine + 1) ? 1 : .65,
                    FontWeight = logicalLine >= 0 && selectedLines.Contains(logicalLine + 1) ? FontWeights.Bold : FontWeights.Normal,
                    TextAlignment = TextAlignment.Right, Width = numbers.Width - 10
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                numbers.Children.Add(label);
                // RichEdit and TextBlock have different space above their glyphs.
                // Match measured baselines instead of aligning their bounding-box tops.
                label.Measure(new Windows.Foundation.Size(label.Width, double.PositiveInfinity));
                Canvas.SetTop(label, baseline - label.BaselineOffset);
                if (visualLine.Move(TextRangeUnit.Line, 1) == 0 || visualLine.StartPosition <= position) break;
            }
            var bottomLine = Document.GetRangeFromPoint(new Windows.Foundation.Point(viewport?.HorizontalOffset ?? 0,
                (viewport?.VerticalOffset ?? 0) + ActualHeight), PointOptions.ClientCoordinates);
            bottomLine.EndOf(TextRangeUnit.Line, false);
            HighlightViewport(low, Math.Min(current.Length, bottomLine.EndPosition));
            if (!ShowWhitespace) return;
            var count = 0;
            var contentLeft = viewport is null ? 0 : viewport.TransformToVisual(whitespace).TransformPoint(new Windows.Foundation.Point(viewport.Padding.Left, 0)).X;
            for (var i = low; i < current.Length && i - low < 20_000 && count < 1500; i++)
            {
                if (Baseline(i) - NumberFontSize > ActualHeight) break;
                var marker = current[i] switch { ' ' => "·", '\t' => "→", '\r' => "¶", _ => null };
                if (marker is null) continue;
                Document.GetRange(i, i).GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Top,
                    PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var point);
                var x = contentLeft + point.X - (viewport?.HorizontalOffset ?? 0);
                if (x < numbers.Width || x >= ActualWidth) continue;
                var label = new TextBlock { Text = marker, FontFamily = FontFamily, FontSize = NumberFontSize, Opacity = .45 };
                whitespace.Children.Add(label); Canvas.SetLeft(label, x); Canvas.SetTop(label, contentTop + point.Y - (viewport?.VerticalOffset ?? 0));
                count++;
            }
        }

        public void Highlight(TextComparisonResult? result, TextDifferenceKind changed, TextCompareDocument? alignment = null)
        {
            highlightVersion++;
            var operations = new List<(int Start, int End, bool Strong)>();
            if (result is not null)
            {
                // Exclude paragraph marks: colouring one also colours its alignment gap.
                var intervals = alignment?.Rows.Where(r => r.IsChange)
                    .Select(r => changed == TextDifferenceKind.Removed ? r.Left : r.Right)
                    .Where(line => line is not null && line.Content.Length > 0)
                    .Select(line => (Start: Position(line), End: Position(line) + line!.Content.Length)).ToArray();
                if (intervals is not null)
                    foreach (var interval in intervals)
                    {
                        operations.Add((interval.Start, interval.End, false));
                    }
                var offset = 0;
                var intervalIndex = 0;
                foreach (var difference in result.Differences)
                {
                    if (difference.Kind != TextDifferenceKind.Equal && difference.Kind != changed) continue;
                    // RichEdit positions count CRLF as one paragraph mark.
                    var length = difference.Text.ReplaceLineEndings("\r").Length;
                    if (difference.Kind == changed)
                    {
                        if (intervals is null) Paint(offset, offset + length);
                        else
                        {
                            while (intervalIndex < intervals.Length && intervals[intervalIndex].End <= offset) intervalIndex++;
                            for (var i = intervalIndex; i < intervals.Length && intervals[i].Start < offset + length; i++)
                                Paint(Math.Max(offset, intervals[i].Start), Math.Min(offset + length, intervals[i].End));
                        }
                    }
                    offset += length;
                }
                void Paint(int start, int end)
                {
                    if (end <= start) return;
                    operations.Add((start, end, true));
                }
            }
            highlights = operations;
            highlightKind = changed;
            ScheduleNumbers();
        }

        private void HighlightViewport(int start, int end)
        {
            var key = (start, end, highlightVersion);
            if (paintedViewport == key) return;
            paintedViewport = key;
            // Keep native formatting bounded by the visible text, rather than creating
            // thousands of RichEdit format runs and live ranges for offscreen lines.
            updating = true;
            Document.BatchDisplayUpdates();
            try
            {
                if (highlighted is not null)
                {
                    highlighted.CharacterFormat = normal;
                    highlighted = null;
                }
                foreach (var operation in highlights)
                {
                    var from = Math.Max(start, operation.Start); var to = Math.Min(end, operation.End);
                    if (to <= from) continue;
                    highlighted ??= Document.GetRange(start, end);
                    var format = Document.GetRange(from, to).CharacterFormat;
                    format.BackgroundColor = highlightKind == TextDifferenceKind.Removed
                        ? operation.Strong ? Windows.UI.Color.FromArgb(255, 255, 195, 195) : Windows.UI.Color.FromArgb(255, 255, 237, 237)
                        : operation.Strong ? Windows.UI.Color.FromArgb(255, 173, 231, 185) : Windows.UI.Color.FromArgb(255, 231, 247, 234);
                    format.ForegroundColor = Colors.Black;
                }
            }
            finally { Document.ApplyDisplayUpdates(); updating = false; }
        }
    }
}
