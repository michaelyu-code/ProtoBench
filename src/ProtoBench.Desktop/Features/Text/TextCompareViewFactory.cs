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
        private readonly LinkedList<(string Text, int Caret)> undo = [], redo = [];
        private bool restoringHistory;
        private long lastEdit;
        private string current = "";
        private readonly Canvas numbers = new() { IsHitTestVisible = false, Width = 44 };
        private readonly Canvas whitespace = new() { IsHitTestVisible = false };
        private HashSet<int> selectedLines = [];
        public bool ShowWhitespace { get; set; }
        private readonly List<int> lineStarts = [0];
        private ScrollViewer? viewport;
        private bool syncingScroll;
        public event Action<ComparisonEditor>? ScrollChanged;
        private bool numbersPending;
        private double lastTextTop = double.NaN;
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
        public double TextTop(int position)
        {
            Document.GetRange(position, position).GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Top,
                PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var point);
            return point.Y - (viewport?.VerticalOffset ?? 0);
        }
        public double LineTop(ComparisonLine? line) => TextTop(Position(line));
        public void ResetSpacing() => Document.GetRange(0, int.MaxValue).ParagraphFormat.SpaceBefore = 0;
        public void AddSpace(int position, double pixels)
        {
            if (pixels > .5) Document.GetRange(position, position).ParagraphFormat.SpaceBefore = (float)(pixels * .75);
        }
        public void SetTailSpace(double pixels)
        {
            var bottom = 8 + Math.Max(0, pixels);
            if (Math.Abs(Padding.Bottom - bottom) > .1) Padding = new Thickness(Padding.Left, Padding.Top, Padding.Right, bottom);
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
                CompositionTarget.Rendering += Rendering;
                UpdateLineStarts();
            };
            Unloaded += (_, _) =>
            {
                CompositionTarget.Rendering -= Rendering;
                if (viewport is not null) viewport.ViewChanged -= ViewChanged;
                viewport = null;
                syncingScroll = false;
            };
            SizeChanged += (_, _) => ScheduleNumbers();
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
                RecordUndo(text, false);
                current = text;
                UpdateLineStarts();
                write(NormalizeEdit?.Invoke(text) ?? text);
            };
        }

        public void SyncText()
        {
            var text = read().ReplaceLineEndings("\r");
            if (text == current) return;
            RecordUndo(text, true);
            updating = true;
            try { Document.SetText(TextSetOptions.None, text); current = text; UpdateLineStarts(); }
            finally { updating = false; }
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
            target.AddLast((current, Document.Selection.StartPosition)); Trim(target);
            updating = restoringHistory = true;
            try
            {
                Document.SetText(TextSetOptions.None, snapshot.Text); current = snapshot.Text;
                UpdateLineStarts();
                var caret = Math.Clamp(snapshot.Caret, 0, current.Length);
                Document.Selection.SetRange(caret, caret);
                write(NormalizeEdit?.Invoke(current) ?? current);
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

        private void Rendering(object? sender, object args)
        {
            if (!IsLoaded || Pane.Visibility != Visibility.Visible) return;
            // RichEdit scrolls its native text surface independently of XAML's ScrollViewer.
            // Observe its text position so wheel, scrollbar and caret scrolling all update the gutter.
            Document.GetRange(0, 0).GetPoint(HorizontalCharacterAlignment.Left, VerticalCharacterAlignment.Top,
                PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var point);
            var top = point.Y - (viewport?.VerticalOffset ?? 0);
            if (top == lastTextTop) return;
            lastTextTop = top;
            DrawNumbers();
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
            // Search character positions so a long wrapped line can start above the viewport
            // without skipping its visible continuations or walking all its offscreen rows.
            var low = 0; var high = current.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (Baseline(middle) < -NumberFontSize * 2) low = middle + 1;
                else high = middle;
            }
            var visualLine = Document.GetRange(low, low);
            visualLine.StartOf(TextRangeUnit.Line, false);
            while (visualLine.StartPosition <= current.Length)
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
            updating = true;
            Document.BatchDisplayUpdates();
            try
            {
                Document.GetRange(0, int.MaxValue).CharacterFormat = normal;
                Document.Selection.CharacterFormat = normal;
                if (result is null) return;
                // Exclude paragraph marks: colouring one also colours its alignment gap.
                var intervals = alignment?.Rows.Where(r => r.IsChange)
                    .Select(r => changed == TextDifferenceKind.Removed ? r.Left : r.Right)
                    .Where(line => line is not null && line.Content.Length > 0)
                    .Select(line => (Start: Position(line), End: Position(line) + line!.Content.Length)).ToArray();
                if (intervals is not null)
                    foreach (var interval in intervals)
                    {
                        var format = Document.GetRange(interval.Start, interval.End).CharacterFormat;
                        format.BackgroundColor = changed == TextDifferenceKind.Removed
                            ? Windows.UI.Color.FromArgb(255, 255, 237, 237) : Windows.UI.Color.FromArgb(255, 231, 247, 234);
                        format.ForegroundColor = Colors.Black;
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
                    var format = Document.GetRange(start, end).CharacterFormat;
                    format.BackgroundColor = changed == TextDifferenceKind.Removed
                        ? Windows.UI.Color.FromArgb(255, 255, 195, 195) : Windows.UI.Color.FromArgb(255, 173, 231, 185);
                    format.ForegroundColor = Colors.Black;
                }
            }
            finally { Document.ApplyDisplayUpdates(); updating = false; ScheduleNumbers(); }
        }
    }
}
