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

public sealed class TextCompareViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "text-compare";

    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (TextCompareWorkspace)workspace;
        var original = new ComparisonEditor("Original text · removals in red", () => vm.Original, text => vm.Original = text);
        var modified = new ComparisonEditor("Modified text · additions in green", () => vm.Modified, text => vm.Modified = text);
        var wrap = new CheckBox { Content = "Wrap text" };
        void SetWrapping() { original.SetWrapping(wrap.IsChecked == true); modified.SetWrapping(wrap.IsChecked == true); }
        wrap.Checked += (_, _) => SetWrapping();
        wrap.Unchecked += (_, _) => SetWrapping();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Original)) original.SyncText();
            if (e.PropertyName == nameof(vm.Modified)) modified.SyncText();
            if (e.PropertyName != nameof(vm.Result)) return;
            original.Highlight(vm.Result, TextDifferenceKind.Removed);
            modified.Highlight(vm.Result, TextDifferenceKind.Added);
        };
        var panes = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        panes.Children.Add(original.Pane);
        panes.Children.Add(modified.Pane);
        bool? stacked = null;
        void Reflow()
        {
            var narrow = panes.ActualWidth < 620;
            if (stacked == narrow) return;
            stacked = narrow;
            panes.ColumnDefinitions.Clear();
            panes.RowDefinitions.Clear();
            panes.ColumnDefinitions.Add(new ColumnDefinition());
            panes.RowDefinitions.Add(new RowDefinition());
            if (narrow) panes.RowDefinitions.Add(new RowDefinition());
            else panes.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(modified.Pane, narrow ? 0 : 1);
            Grid.SetRow(modified.Pane, narrow ? 1 : 0);
        }
        panes.SizeChanged += (_, _) => Reflow();
        Reflow();
        var content = new Grid { RowSpacing = 8 };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(Ui.Row(Ui.Primary(new Button { Content = "Compare text", Command = vm.CompareCommand }),
            new Button { Content = "Swap", Command = vm.SwapCommand }, new Button { Content = "Clear", Command = vm.ClearCommand },
            wrap, Ui.Progress(vm), Ui.BoundText(vm, nameof(vm.Status))));
        var feedback = ToolLayout.Stack(Ui.BoundText(vm, nameof(vm.Error)), Ui.BoundText(vm, nameof(vm.Summary)));
        Grid.SetRow(feedback, 1); content.Children.Add(feedback);
        Grid.SetRow(panes, 2); content.Children.Add(panes);
        return ToolLayout.FittedPage(vm.Title, "Edit and compare both texts here. Red = removed; green = added.", content,
            ToolLayout.Help(Ui.Text("Paste the original and modified text, then select Compare text. Differences are highlighted directly in the editable inputs. Both panes fill the available space and scroll independently, with line numbers for each actual text line. Select Wrap text to wrap long lines in both editors; wrapped continuations do not get extra line numbers. Wrapping only changes the display. Comparison is case-sensitive and includes whitespace. The editor uses a single paragraph mark for each line ending. Emoji and combining characters stay together. Each input supports up to 100,000 characters. Large changes may be highlighted as whole blocks. Editing either input clears the previous highlights; select Compare text to refresh them.")));
    }

    private sealed class ComparisonEditor : RichEditBox
    {
        private readonly Func<string> read;
        private readonly Action<string> write;
        private readonly ITextCharacterFormat normal;
        private bool updating;
        private string current = "";
        private readonly Canvas numbers = new() { IsHitTestVisible = false, Width = 44 };
        private readonly List<int> lineStarts = [0];
        private ScrollViewer? viewport;
        private bool numbersPending;
        private double lastTextTop = double.NaN;
        private double NumberFontSize => normal.Size * 96d / 72d;
        public Grid Pane { get; } = new() { RowSpacing = 6 };

        public ComparisonEditor(string title, Func<string> read, Action<string> write)
        {
            this.read = read;
            this.write = write;
            AcceptsReturn = true;
            MaxLength = TextComparison.MaxCharacters;
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
            numbers.HorizontalAlignment = HorizontalAlignment.Left;
            body.Children.Add(numbers);
            Grid.SetRow(body, 1); Pane.Children.Add(body);
            Loaded += (_, _) =>
            {
                viewport = FindViewport(this);
                CompositionTarget.Rendering += Rendering;
                UpdateLineStarts();
            };
            Unloaded += (_, _) =>
            {
                CompositionTarget.Rendering -= Rendering;
                viewport = null;
            };
            SizeChanged += (_, _) => ScheduleNumbers();
            SyncText();
            TextChanged += (_, _) =>
            {
                if (updating) return;
                Document.GetText(TextGetOptions.None, out var text);
                // RichEdit always includes one final, implicit paragraph mark.
                if (text.EndsWith('\r')) text = text[..^1];
                if (text == current) return;
                current = text;
                UpdateLineStarts();
                write(text);
            };
        }

        public void SyncText()
        {
            var text = read().ReplaceLineEndings("\r");
            if (text == current) return;
            updating = true;
            try { Document.SetText(TextSetOptions.None, text); current = text; UpdateLineStarts(); }
            finally { updating = false; }
        }

        public void SetWrapping(bool enabled)
        {
            TextWrapping = enabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
            ScrollViewer.SetHorizontalScrollBarVisibility(this, enabled ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollMode(this, enabled ? ScrollMode.Disabled : ScrollMode.Auto);
            ScheduleNumbers();
        }

        private void Rendering(object? sender, object args)
        {
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
            if (!IsLoaded || ActualHeight <= 0) return;
            numbers.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, numbers.ActualWidth, ActualHeight) };
            numbers.Children.Clear();
            // RichEdit's client coordinates start inside the ScrollViewer's padding.
            var contentTop = viewport is null ? 0 : viewport.TransformToVisual(numbers)
                .TransformPoint(new Windows.Foundation.Point(0, viewport.Padding.Top)).Y;
            double Baseline(int line)
            {
                Document.GetRange(lineStarts[line], lineStarts[line]).GetPoint(HorizontalCharacterAlignment.Left,
                    VerticalCharacterAlignment.Baseline, PointOptions.ClientCoordinates | PointOptions.AllowOffClient, out var point);
                return contentTop + point.Y - (viewport?.VerticalOffset ?? 0);
            }
            // Find the first visible logical line without measuring every line in a large input.
            var low = 0; var high = lineStarts.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (Baseline(middle) < -NumberFontSize * 2) low = middle + 1;
                else high = middle;
            }
            for (var line = low; line < lineStarts.Count; line++)
            {
                var baseline = Baseline(line);
                if (baseline - NumberFontSize >= ActualHeight) break;
                var label = new TextBlock
                {
                    Text = (line + 1).ToString(), FontFamily = FontFamily, FontSize = NumberFontSize,
                    Opacity = .65, TextAlignment = TextAlignment.Right, Width = numbers.Width - 10
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(label, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                numbers.Children.Add(label);
                // RichEdit and TextBlock have different space above their glyphs.
                // Match measured baselines instead of aligning their bounding-box tops.
                label.Measure(new Windows.Foundation.Size(label.Width, double.PositiveInfinity));
                Canvas.SetTop(label, baseline - label.BaselineOffset);
            }
        }

        public void Highlight(TextComparisonResult? result, TextDifferenceKind changed)
        {
            updating = true;
            Document.BatchDisplayUpdates();
            try
            {
                Document.GetRange(0, int.MaxValue).CharacterFormat = normal;
                Document.Selection.CharacterFormat = normal;
                if (result is null) return;
                var offset = 0;
                foreach (var difference in result.Differences)
                {
                    if (difference.Kind != TextDifferenceKind.Equal && difference.Kind != changed) continue;
                    // RichEdit positions count CRLF as one paragraph mark.
                    var length = difference.Text.ReplaceLineEndings("\r").Length;
                    if (difference.Kind == changed)
                    {
                        var format = Document.GetRange(offset, offset + length).CharacterFormat;
                        format.BackgroundColor = changed == TextDifferenceKind.Removed
                            ? Windows.UI.Color.FromArgb(255, 255, 210, 210) : Windows.UI.Color.FromArgb(255, 196, 240, 205);
                        format.ForegroundColor = Colors.Black;
                    }
                    offset += length;
                }
            }
            finally { Document.ApplyDisplayUpdates(); updating = false; ScheduleNumbers(); }
        }
    }
}
