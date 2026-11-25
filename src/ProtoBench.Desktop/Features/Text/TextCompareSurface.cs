using System.ComponentModel;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Infrastructure.Files;
using ProtoBench.Utilities.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace ProtoBench.Desktop.Features.Text;

public sealed partial class TextCompareViewFactory
{
    private sealed class ComparisonSurface
    {
        private readonly TextCompareWorkspace vm;
        private readonly FileDialogService dialogs;
        private readonly ComparisonEditor left, right;
        private readonly TextCompareReview review;
        private readonly Grid panes = new() { ColumnSpacing = 4 };
        private readonly Canvas gutter = new() { Width = 42, IsHitTestVisible = true };
        private readonly Canvas overview = new() { Width = 18, Background = new SolidColorBrush(Colors.Transparent) };
        private readonly Button previous, next, copyLeft, copyRight, undo, narrowCopyLeft, narrowCopyRight, findButton;
        private readonly TextBlock navigation = Ui.Text("No changes"), ruleCount = Ui.Text(""), feedback = Ui.Text(""), operation = Ui.Text("");
        private readonly ToggleMenuFlyoutItem wrap = new() { Text = "Wrap text" }, sync = new() { Text = "Synchronized scrolling", IsChecked = true };
        private readonly ToggleMenuFlyoutItem automatic = new() { Text = "Compare automatically", IsChecked = true };
        private readonly TextBlock displayNotice = Ui.Text("");
        private int contextRadius = -1;
        private int fontSize = 12;
        private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(400) };
        private bool active, alignPending, stacked;
        private int alignmentVersion;
        private int navigationVersion;
        private double layoutWidth = -1;
        private TextCompareDocument? indexedAlignment;
        private readonly Dictionary<int, int> leftChanges = [], rightChanges = [];
        private IReadOnlyList<TextCompareOverviewMark> overviewMarks = [];
        private IReadOnlyList<(int ChangeIndex, double Height)> overviewRows = [];
        private readonly FileSide leftFile = new(), rightFile = new();
        private ComparisonEditor? scrollLeader;
        private (bool Left, int Line)? highlightedLine;
        public FrameworkElement Root { get; }

        public ComparisonSurface(TextCompareWorkspace vm, FileDialogService dialogs)
        {
            this.vm = vm; this.dialogs = dialogs;
            review = new TextCompareReview(index => vm.SelectedChange = index) { Visibility = Visibility.Collapsed };
            left = new ComparisonEditor("Original · removed", () => vm.Original, text => vm.Original = text);
            right = new ComparisonEditor("Modified · added", () => vm.Modified, text => vm.Modified = text);
            left.NormalizeEdit = text => { leftFile.MixedLineEndings = false; return text.ReplaceLineEndings(leftFile.NewLine); };
            right.NormalizeEdit = text => { rightFile.MixedLineEndings = false; return text.ReplaceLineEndings(rightFile.NewLine); };
            previous = Ui.Button("← Previous", () => Navigate(-1));
            next = Ui.Button("Next →", () => Navigate(1));
            copyLeft = Ui.Button("←", () => Copy(false));
            copyRight = Ui.Button("→", () => Copy(true));
            foreach (var button in new[] { copyLeft, copyRight }) { button.MinWidth = 32; button.Padding = new Thickness(4); }
            ToolTipService.SetToolTip(copyLeft, "Copy selected change to original (Ctrl+Alt+Left)");
            ToolTipService.SetToolTip(copyRight, "Copy selected change to modified (Ctrl+Alt+Right)");
            gutter.Children.Add(copyRight); gutter.Children.Add(copyLeft);
            undo = Ui.Button("Undo copy", () => { vm.UndoCopy(); RequestCompare(true); });
            narrowCopyRight = Ui.Button("Copy →", () => Copy(true));
            narrowCopyLeft = Ui.Button("← Copy", () => Copy(false));
            narrowCopyRight.Visibility = narrowCopyLeft.Visibility = Visibility.Collapsed;
            var rules = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
            AddRule("Ignore case", () => vm.Options.IgnoreCase, value => vm.Options = vm.Options with { IgnoreCase = value });
            AddRule("Ignore whitespace", () => vm.Options.IgnoreWhitespace, value => vm.Options = vm.Options with { IgnoreWhitespace = value });
            AddRule("Ignore blank lines", () => vm.Options.IgnoreBlankLines, value => vm.Options = vm.Options with { IgnoreBlankLines = value });
            var view = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
            view.Items.Add(wrap); view.Items.Add(sync);
            var fontSizes = new MenuFlyoutSubItem { Text = "Font size" };
            foreach (var size in new[] { 12, 14, 16, 18, 20, 22, 24, 28, 32 })
            {
                var item = new RadioMenuFlyoutItem { Text = size == 12 ? "12 (default)" : size.ToString(),
                    GroupName = $"{vm.InstanceId}-font-size", IsChecked = size == fontSize };
                item.Click += (_, _) =>
                {
                    fontSize = size;
                    left.SetFontSize(size); right.SetFontSize(size);
                    ApplyFilter(); ScheduleAlignment();
                };
                fontSizes.Items.Add(item);
            }
            view.Items.Add(fontSizes);
            var whitespace = new ToggleMenuFlyoutItem { Text = "Show whitespace" };
            whitespace.Click += (_, _) => { left.ShowWhitespace = right.ShowWhitespace = whitespace.IsChecked; ApplyFilter(); Refresh(); };
            view.Items.Add(whitespace); view.Items.Add(new MenuFlyoutSeparator());
            foreach (var (label, radius) in new[] { ("All lines · editable", -1), ("Changes with 3 context lines", 3), ("Differences only", 0) })
            {
                var item = new RadioMenuFlyoutItem { Text = label, GroupName = vm.InstanceId.ToString(), IsChecked = radius == -1 };
                item.Click += (_, _) => { contextRadius = radius; ApplyFilter(); ScheduleAlignment(); };
                view.Items.Add(item);
            }
            view.Items.Add(new MenuFlyoutSeparator()); view.Items.Add(automatic);
            wrap.Click += (_, _) => { left.SetWrapping(wrap.IsChecked); right.SetWrapping(wrap.IsChecked); ApplyFilter(); ScheduleAlignment(); };
            sync.Click += (_, _) => { if (sync.IsChecked) right.SyncScrollFrom(left); };
            automatic.Click += (_, _) => { if (automatic.IsChecked) RequestCompare(); else debounce.Stop(); };
            left.ScrollChanged += editor => { scrollLeader = editor; if (sync.IsChecked) right.SyncScrollFrom(editor); };
            right.ScrollChanged += editor => { scrollLeader = editor; if (sync.IsChecked) left.SyncScrollFrom(editor); };
            left.ViewportChanged += UpdateGutter; right.ViewportChanged += UpdateGutter;
            left.GeometryChanged += ScheduleAlignment; right.GeometryChanged += ScheduleAlignment;
            left.FindRequested += ShowFind; right.FindRequested += ShowFind;
            left.NavigateRequested += Navigate; right.NavigateRequested += Navigate;
            left.CopyRequested += Copy; right.CopyRequested += Copy;
            left.LineClicked += line => ToggleLineHighlight(true, line);
            right.LineClicked += line => ToggleLineHighlight(false, line);
            review.LineClicked += ToggleLineHighlight;
            left.LineHighlightGeometryChanged += (line, top, height) => UpdateLineHighlightGap(true, line, top, height);
            right.LineHighlightGeometryChanged += (line, top, height) => UpdateLineHighlightGap(false, line, top, height);
            left.SelectionChanged += (_, _) => SelectAtCaret(left, true);
            right.SelectionChanged += (_, _) => SelectAtCaret(right, false);
            var more = new MenuFlyout();
            var clear = new MenuFlyoutItem { Text = "Clear both inputs", Command = vm.ClearCommand };
            more.Items.Add(clear);
            var export = ComparisonReportExport.CreateButton(vm, dialogs, () => vm.HasResult, vm.CreateDifferencesReport, "text-compare-differences", feedback);
            export.Content = "Export report…";
            findButton = Ui.Button("Find / replace…", ShowFind);
            var toolbar = Ui.Row(Ui.Primary(Ui.AsyncButton("Compare", async () =>
                { await Task.WhenAll(left.FlushAsync(), right.FlushAsync()); await vm.CompareCommand.ExecuteAsync(null); }, ex => feedback.Text = ex.Message)),
                Ui.Button("Swap", Swap),
                new DropDownButton { Content = "Rules", Flyout = rules }, ruleCount,
                new DropDownButton { Content = "View", Flyout = view }, findButton, export,
                new DropDownButton { Content = "More", Flyout = more }, Ui.Progress(vm), operation);
            panes.ColumnDefinitions.Add(new ColumnDefinition());
            panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panes.ColumnDefinitions.Add(new ColumnDefinition());
            panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panes.RowDefinitions.Add(new RowDefinition());
            panes.Children.Add(left.Pane); panes.Children.Add(gutter); panes.Children.Add(right.Pane); panes.Children.Add(overview);
            panes.Children.Add(review); Grid.SetColumnSpan(review, 3);
            Grid.SetColumn(gutter, 1); Grid.SetColumn(right.Pane, 2); Grid.SetColumn(overview, 3);
            AddFileHeader(left, leftFile, true); AddFileHeader(right, rightFile, false);
            left.SizeChanged += (_, _) => UpdateOverviewBounds();
            right.SizeChanged += (_, _) => UpdateOverviewBounds();
            review.Viewport.SizeChanged += (_, _) => UpdateOverviewBounds();
            review.OverviewChanged += () => { if (review.Visibility == Visibility.Visible) { UpdateOverviewBounds(); DrawOverview(); } };
            overview.SizeChanged += (_, _) => DrawOverview();
            overview.PointerPressed += (_, e) =>
            {
                if (vm.Alignment is not { Changes.Count: > 0 }) return;
                var target = TextCompareOverview.FindTarget(overviewMarks, overviewRows, e.GetCurrentPoint(overview).Position.Y);
                if (target is not { } hit) return;
                vm.SelectedChange = hit.ChangeIndex;
                Reveal(hit.RowIndex); e.Handled = true;
            };
            ToolTipService.SetToolTip(overview, "Comparison overview · click to jump to that position in a change");
            var content = new Grid { RowSpacing = 8 };
            for (var i = 0; i < 4; i++) content.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            content.Children.Add(toolbar);
            var messages = ToolLayout.Stack(Ui.BoundText(vm, nameof(vm.Error)), feedback, displayNotice);
            Grid.SetRow(messages, 1); content.Children.Add(messages);
            Grid.SetRow(panes, 2); content.Children.Add(panes);
            var footer = Ui.Row(previous, next, navigation, Ui.BoundText(vm, nameof(vm.Summary)), undo, narrowCopyRight, narrowCopyLeft);
            Grid.SetRow(footer, 3); content.Children.Add(footer);
            Root = ToolLayout.FittedPage(vm.Title, "", content,
                ToolLayout.Help(Ui.Text("Paste or open text on either side. Changes refresh after you pause typing. F7 / Shift+F7 moves between changes. Click a line number to highlight the corresponding row in both panes; click again to clear it. Use the centre arrows to copy the selected change; Undo copy restores both inputs until another edit. Spacer gaps align corresponding lines without adding characters. Rules ignore case, whitespace or blank lines without changing either input. Wrapping and synchronized scrolling are in View. Open supports UTF-8 and BOM-marked UTF-16; save preserves the detected encoding and newline convention. Mixed line endings are retained until editing, which uses the first line-ending convention. Inputs remain local and are not saved automatically.")));
            AddKey(VirtualKey.F7, VirtualKeyModifiers.None, () => Navigate(1));
            AddKey(VirtualKey.F7, VirtualKeyModifiers.Shift, () => Navigate(-1));
            AddKey(VirtualKey.Right, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => Copy(true));
            AddKey(VirtualKey.Left, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, () => Copy(false));
            AddKey(VirtualKey.F, VirtualKeyModifiers.Control, ShowFind);
            panes.SizeChanged += (_, _) => Reflow();
            Root.Loaded += (_, _) =>
            {
                if (active) return;
                active = true; vm.PropertyChanged += Changed;
                panes.LayoutUpdated += OverviewLayoutUpdated;
                left.SyncText(); right.SyncText();
                left.Highlight(vm.Result, TextDifferenceKind.Removed, vm.Alignment);
                right.Highlight(vm.Result, TextDifferenceKind.Added, vm.Alignment);
                Refresh(); ApplyFilter(); ScheduleAlignment();
                Root.DispatcherQueue.TryEnqueue(UpdateOverviewBounds);
                if (!vm.HasResult && (vm.Original.Length > 0 || vm.Modified.Length > 0)) RequestCompare();
            };
            Root.Unloaded += (_, _) =>
            {
                active = false; alignmentVersion++; debounce.Stop(); vm.PropertyChanged -= Changed;
                panes.LayoutUpdated -= OverviewLayoutUpdated;
            };
            debounce.Tick += async (_, _) =>
            {
                debounce.Stop();
                if (!active) return;
                if (vm.IsBusy) { debounce.Start(); return; }
                await vm.CompareCommand.ExecuteAsync(null);
            };
            Refresh();

            void AddRule(string title, Func<bool> get, Action<bool> set)
            {
                var item = new ToggleMenuFlyoutItem { Text = title, IsChecked = get() };
                item.Click += (_, _) => { set(item.IsChecked); RequestCompare(true); };
                rules.Items.Add(item);
            }
        }

        private void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(vm.Original) or nameof(vm.Modified))
            {
                highlightedLine = null; ApplyLineHighlight();
            }
            if (e.PropertyName == nameof(vm.IsBusy) && vm.IsBusy) debounce.Stop();
            if (e.PropertyName == nameof(vm.Status)) operation.Text = vm.Status == "Working…" ? "Comparing…" : vm.Status is "Ready" or "Completed" or "Cancelled" ? "" : vm.Status;
            if (e.PropertyName == nameof(vm.Original)) { alignmentVersion++; left.SyncText(); UpdateFile(leftFile, vm.Original); RequestCompare(); }
            if (e.PropertyName == nameof(vm.Modified)) { alignmentVersion++; right.SyncText(); UpdateFile(rightFile, vm.Modified); RequestCompare(); }
            if (e.PropertyName == nameof(vm.Options)) RequestCompare();
            if (e.PropertyName == nameof(vm.Result))
            {
                left.Highlight(vm.Result, TextDifferenceKind.Removed, vm.Alignment);
                right.Highlight(vm.Result, TextDifferenceKind.Added, vm.Alignment);
                ApplyFilter();
                ScheduleAlignment();
            }
            if (e.PropertyName is nameof(vm.Alignment) or nameof(vm.SelectedChange) or nameof(vm.CanUndoCopy) or nameof(vm.Options)) Refresh();
        }

        private void RequestCompare(bool force = false)
        {
            if (!active || !automatic.IsChecked && !force) return;
            debounce.Stop(); debounce.Start();
        }
        private void Navigate(int direction) { vm.Navigate(direction); Reveal(); }
        private async void Reveal(int? rowIndex = null)
        {
            var version = ++navigationVersion;
            if (vm.Alignment is not { } aligned || vm.SelectedChange < 0 || vm.SelectedChange >= aligned.Changes.Count) return;
            if (contextRadius >= 0) { review.SelectChange(vm.SelectedChange, true, rowIndex); return; }
            var change = aligned.Changes[vm.SelectedChange];
            var targetRow = rowIndex is { } index && index >= change.FirstRow && index < change.FirstRow + change.RowCount
                ? index : change.FirstRow;
            var row = aligned.Rows[targetRow];
            var primary = row.Left is null ? right : left;
            var secondary = primary == left ? right : left;
            scrollLeader = primary;
            try
            {
                await primary.RevealAsync(row.Left ?? row.Right, primary == left ? change.LeftStart : change.RightStart);
                if (!active || version != navigationVersion || !ReferenceEquals(aligned, vm.Alignment)) return;
                // Both panes share aligned pixel geometry. A missing source line
                // is a visual gap here, not a request to jump to end of file.
                if (!stacked) secondary.SyncScrollFrom(primary);
                else await secondary.RevealAsync(primary == left ? row.Right : row.Left,
                    primary == left ? change.RightStart : change.LeftStart);
                UpdateGutter();
            }
            catch (Exception ex) { feedback.Text = $"Could not jump to the change: {ex.Message}"; }
        }
        private async void Copy(bool toRight)
        {
            try { await Task.WhenAll(left.FlushAsync(), right.FlushAsync()); vm.CopyChange(toRight); RequestCompare(true); }
            catch (Exception ex) { feedback.Text = ex.Message; }
        }
        private void SelectAtCaret(ComparisonEditor editor, bool isLeft)
        {
            if (vm.Alignment is null) return;
            if ((isLeft ? leftChanges : rightChanges).TryGetValue(editor.CaretLine, out var change)) vm.SelectedChange = change;
        }
        private ComparisonRow? HighlightedRow => highlightedLine is { } selected && vm.Alignment is { } aligned
            ? aligned.Rows.FirstOrDefault(row => (selected.Left ? row.Left : row.Right)?.Number == selected.Line) : null;
        private void ToggleLineHighlight(bool isLeft, int line)
        {
            var row = HighlightedRow;
            var same = highlightedLine is { } selected && (selected.Left == isLeft && selected.Line == line
                || (isLeft ? row?.Left : row?.Right)?.Number == line);
            highlightedLine = same ? null : (isLeft, line);
            ApplyLineHighlight();
        }
        private void ApplyLineHighlight()
        {
            var row = HighlightedRow;
            var enabled = highlightedLine is not null;
            left.HighlightLine(row?.Left?.Number ?? (vm.Alignment is null ? highlightedLine?.Line : null), enabled);
            right.HighlightLine(row?.Right?.Number ?? (vm.Alignment is null ? highlightedLine?.Line : null), enabled);
            review.HighlightLine(row);
        }
        private void UpdateLineHighlightGap(bool isLeft, int line, double top, double height)
        {
            if (highlightedLine is not { } selected || selected.Left != isLeft || selected.Line != line || HighlightedRow is not { } row) return;
            if (isLeft && row.Right is null) right.HighlightLineGeometry(top, height);
            if (!isLeft && row.Left is null) left.HighlightLineGeometry(top, height);
        }
        private void Refresh()
        {
            if (!ReferenceEquals(indexedAlignment, vm.Alignment))
            {
                indexedAlignment = vm.Alignment;
                ApplyLineHighlight();
                leftChanges.Clear(); rightChanges.Clear();
                if (indexedAlignment is not null)
                    foreach (var row in indexedAlignment.Rows)
                    {
                        if (!row.IsChange) continue;
                        if (row.Left is { } a) leftChanges[a.Number] = row.ChangeIndex;
                        if (row.Right is { } b) rightChanges[b.Number] = row.ChangeIndex;
                    }
            }
            var hasChanges = vm.Alignment?.Changes.Count > 0;
            previous.IsEnabled = next.IsEnabled = copyLeft.IsEnabled = copyRight.IsEnabled = narrowCopyLeft.IsEnabled = narrowCopyRight.IsEnabled = hasChanges;
            navigation.Text = vm.Alignment is null ? "Compare to find changes" : vm.Navigation;
            ruleCount.Text = vm.Options.ActiveCount == 0 ? "" : $"{vm.Options.ActiveCount} ignore rules active";
            undo.Visibility = vm.CanUndoCopy ? Visibility.Visible : Visibility.Collapsed;
            narrowCopyLeft.Visibility = narrowCopyRight.Visibility = stacked || contextRadius >= 0 ? Visibility.Visible : Visibility.Collapsed;
            var selectedRows = vm.Alignment is { } aligned && vm.SelectedChange >= 0 && vm.SelectedChange < aligned.Changes.Count
                ? aligned.Rows.Skip(aligned.Changes[vm.SelectedChange].FirstRow).Take(aligned.Changes[vm.SelectedChange].RowCount).ToArray() : [];
            left.MarkSelected(selectedRows.Where(r => r.Left is not null).Select(r => r.Left!.Number));
            right.MarkSelected(selectedRows.Where(r => r.Right is not null).Select(r => r.Right!.Number));
            if (contextRadius >= 0) review.SelectChange(vm.SelectedChange, false);
            DrawOverview(); UpdateGutter();
        }
        private void ApplyFilter()
        {
            if (contextRadius < 0)
            {
                review.Visibility = Visibility.Collapsed;
                left.Pane.Visibility = right.Pane.Visibility = Visibility.Visible;
                narrowCopyLeft.Visibility = narrowCopyRight.Visibility = stacked ? Visibility.Visible : Visibility.Collapsed;
                displayNotice.Text = ""; return;
            }
            if (vm.Alignment is not { } aligned)
            {
                left.Pane.Visibility = right.Pane.Visibility = Visibility.Collapsed;
                review.Visibility = Visibility.Visible; review.SetInteractive(false);
                displayNotice.Text = "Comparison pending · choose View → All lines to edit.";
                return;
            }
            review.SetInteractive(true);
            var visible = new HashSet<int>();
            foreach (var change in aligned.Changes)
                for (var row = Math.Max(0, change.FirstRow - contextRadius); row < Math.Min(aligned.Rows.Count, change.FirstRow + change.RowCount + contextRadius); row++) visible.Add(row);
            left.Pane.Visibility = right.Pane.Visibility = Visibility.Collapsed;
            copyLeft.Visibility = copyRight.Visibility = Visibility.Collapsed;
            narrowCopyLeft.Visibility = narrowCopyRight.Visibility = Visibility.Visible;
            review.Visibility = Visibility.Visible;
            review.Update(aligned, visible, vm.SelectedChange, wrap.IsChecked, left.ShowWhitespace, fontSize);
            displayNotice.Text = $"{aligned.Rows.Count - visible.Count} aligned rows hidden · choose View → All lines to edit.";
        }
        private void OverviewLayoutUpdated(object? sender, object e)
        {
            if (review.Visibility == Visibility.Visible) review.RefreshOverviewLayout();
            UpdateOverviewBounds();
        }
        private void UpdateOverviewBounds()
        {
            if (!panes.IsLoaded) return;
            FrameworkElement surface = review.Visibility == Visibility.Visible ? review.Viewport : stacked ? left : right;
            if (!surface.IsLoaded || surface.ActualHeight <= 0) return;
            var surfaceTop = Math.Clamp(surface.TransformToVisual(panes).TransformPoint(new Windows.Foundation.Point()).Y, 0, panes.ActualHeight);
            var top = Math.Min(panes.ActualHeight, surfaceTop + (review.Visibility == Visibility.Visible ? review.OverviewInset : 0));
            var bottom = stacked && review.Visibility != Visibility.Visible ? 0 : Math.Max(0, panes.ActualHeight - surfaceTop - surface.ActualHeight);
            if (Math.Abs(overview.Margin.Top - top) > .1 || Math.Abs(overview.Margin.Bottom - bottom) > .1)
                overview.Margin = new Thickness(0, top, 0, bottom);
        }
        private void DrawOverview()
        {
            overview.Children.Clear();
            overviewMarks = [];
            overviewRows = [];
            overview.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, overview.ActualWidth, overview.ActualHeight) };
            if (vm.Alignment is not { } aligned || overview.ActualHeight <= 0) return;
            // Compress long documents, but never stretch a short document to fill the editor.
            if (review.Visibility == Visibility.Visible) overviewRows = review.OverviewRows;
            else
            {
                var rowHeight = Math.Max(1, right.LineHeight);
                overviewRows = aligned.Rows.Select(row => (row.ChangeIndex, rowHeight)).ToArray();
            }
            overviewMarks = TextCompareOverview.Create(overviewRows, overview.ActualHeight);
            // Bucket overview marks by pixel to keep rendering bounded for very large inputs.
            foreach (var bucket in overviewMarks.GroupBy(mark => (int)mark.Top))
            {
                var selected = bucket.Any(mark => mark.ChangeIndex == vm.SelectedChange);
                var top = bucket.Min(mark => mark.Top);
                var rect = new Rectangle { Width = 14, Height = bucket.Max(mark => mark.Top + mark.Height) - top,
                    Fill = new SolidColorBrush(selected ? Colors.DodgerBlue : Colors.DarkOrange), IsHitTestVisible = false };
                Canvas.SetLeft(rect, 2); Canvas.SetTop(rect, top);
                overview.Children.Add(rect);
            }
        }
        private void UpdateGutter()
        {
            gutter.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, 42, Math.Max(0, gutter.ActualHeight)) };
            if (!gutter.IsLoaded || stacked || contextRadius >= 0 || vm.Alignment is not { } aligned || vm.SelectedChange < 0 || vm.SelectedChange >= aligned.Changes.Count)
            { copyLeft.Visibility = copyRight.Visibility = Visibility.Collapsed; return; }
            var row = aligned.Rows[aligned.Changes[vm.SelectedChange].FirstRow];
            var editor = row.Left is null ? right : left;
            var line = row.Left ?? row.Right;
            var top = editor.TransformToVisual(gutter).TransformPoint(new Windows.Foundation.Point(0, editor.LineTop(line))).Y;
            copyLeft.Visibility = copyRight.Visibility = Visibility.Visible;
            Canvas.SetTop(copyRight, top); Canvas.SetTop(copyLeft, top + 34);
            Canvas.SetLeft(copyLeft, 5); Canvas.SetLeft(copyRight, 5);
        }
        private void Reflow()
        {
            var nextStacked = panes.ActualWidth < 620;
            var changed = stacked != nextStacked;
            var widthChanged = Math.Abs(layoutWidth - panes.ActualWidth) > .5;
            if (layoutWidth >= 0 && !changed)
            {
                layoutWidth = panes.ActualWidth;
                if (widthChanged && wrap.IsChecked) ScheduleAlignment();
                return;
            }
            layoutWidth = panes.ActualWidth;
            stacked = nextStacked;
            narrowCopyLeft.Visibility = narrowCopyRight.Visibility = stacked || contextRadius >= 0 ? Visibility.Visible : Visibility.Collapsed;
            panes.RowDefinitions.Clear(); panes.RowDefinitions.Add(new RowDefinition());
            if (stacked) panes.RowDefinitions.Add(new RowDefinition());
            panes.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Auto;
            panes.ColumnDefinitions[2].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(right.Pane, stacked ? 0 : 2); Grid.SetRow(right.Pane, stacked ? 1 : 0);
            Grid.SetRowSpan(overview, stacked ? 2 : 1);
            Grid.SetRowSpan(review, stacked ? 2 : 1);
            ScheduleAlignment();
        }
        private void ScheduleAlignment()
        {
            alignmentVersion++;
            if (alignPending || !active) return;
            alignPending = true;
            Root.DispatcherQueue.TryEnqueue(async () =>
            {
                var version = alignmentVersion;
                try { await AlignEditors(); }
                catch (Exception ex) { feedback.Text = $"Could not align editor layout: {ex.Message}"; }
                finally
                {
                    alignPending = false;
                    if (active) UpdateGutter();
                    if (active && version != alignmentVersion) ScheduleAlignment();
                }
            });
        }
        private async Task AlignEditors()
        {
            if (stacked || contextRadius >= 0 || vm.Alignment is not { } aligned)
            {
                left.ResetSpacing(); right.ResetSpacing(); return;
            }
            var version = alignmentVersion;
            var measurements = await Task.WhenAll(left.MeasureLinesAsync(), right.MeasureLinesAsync());
            if (!active || version != alignmentVersion || !ReferenceEquals(aligned, vm.Alignment)) return;
            var layout = TextCompareVirtualLayout.Create(aligned, measurements[0], measurements[1]);
            left.SetSpacing(layout.Left); right.SetSpacing(layout.Right);
            if (sync.IsChecked)
            {
                if (scrollLeader == right) left.SyncScrollFrom(right);
                else right.SyncScrollFrom(left);
            }
        }
        private void AddKey(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
        {
            var shortcut = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
            shortcut.Invoked += (_, e) => { action(); e.Handled = true; };
            Root.KeyboardAccelerators.Add(shortcut);
        }

        private void ShowFind()
        {
            if (contextRadius >= 0) { feedback.Text = "Choose View → All lines to find and replace in the editable inputs."; return; }
            var find = new TextBox { PlaceholderText = "Find text", MinWidth = 220 };
            var replacement = new TextBox { PlaceholderText = "Replace with" };
            var side = new ComboBox { ItemsSource = new[] { "Original", "Modified" }, SelectedIndex = 1 };
            var message = Ui.Text("Case-insensitive literal search. Search wraps at the end.");
            ComparisonEditor Editor() => side.SelectedIndex == 0 ? left : right;
            var flyout = new Flyout();
            flyout.Content = ToolLayout.Stack(side, find, replacement,
                Ui.Row(Ui.AsyncButton("Find next", async () => { if (!await Editor().FindAsync(find.Text)) message.Text = "No match found."; }),
                    Ui.AsyncButton("Replace", async () =>
                    {
                        if (find.Text.Length == 0) { message.Text = "Enter text to find."; return; }
                        var editor = Editor();
                        if (!string.Equals(editor.SelectedText, find.Text, StringComparison.OrdinalIgnoreCase))
                        { if (!await editor.FindAsync(find.Text)) message.Text = "No match found."; return; }
                        editor.ReplaceSelection(replacement.Text);
                    })), message);
            flyout.ShowAt(findButton);
        }

        private async void Swap()
        {
            try { await Task.WhenAll(left.FlushAsync(), right.FlushAsync()); }
            catch (Exception ex) { feedback.Text = ex.Message; return; }
            (leftFile.Path, rightFile.Path) = (rightFile.Path, leftFile.Path);
            (leftFile.SavedText, rightFile.SavedText) = (rightFile.SavedText, leftFile.SavedText);
            (leftFile.Encoding, rightFile.Encoding) = (rightFile.Encoding, leftFile.Encoding);
            (leftFile.NewLine, rightFile.NewLine) = (rightFile.NewLine, leftFile.NewLine);
            (leftFile.ReadEncoding, rightFile.ReadEncoding) = (rightFile.ReadEncoding, leftFile.ReadEncoding);
            (leftFile.SavedEncoding, rightFile.SavedEncoding) = (rightFile.SavedEncoding, leftFile.SavedEncoding);
            (leftFile.MixedLineEndings, rightFile.MixedLineEndings) = (rightFile.MixedLineEndings, leftFile.MixedLineEndings);
            vm.SwapCommand.Execute(null);
            UpdateFile(leftFile, vm.Original); UpdateFile(rightFile, vm.Modified);
            RequestCompare(true);
        }

        private sealed class FileSide
        {
            public string? Path;
            public string SavedText = "";
            public Encoding Encoding = new UTF8Encoding(false, true);
            public Encoding? ReadEncoding;
            public string SavedEncoding = TextCompareFile.EncodingKey(new UTF8Encoding(false, true));
            public string NewLine = "\n";
            public bool MixedLineEndings;
            public TextBlock Label = Ui.Text("Pasted text");
            public DropDownButton Format = new();
        }
        private static void UpdateFile(FileSide side, string text)
        {
            side.Label.Text = side.Path is null ? "Pasted text" : $"{System.IO.Path.GetFileName(side.Path)}{(text == side.SavedText && TextCompareFile.EncodingKey(side.Encoding) == side.SavedEncoding ? "" : " • unsaved")}";
            ToolTipService.SetToolTip(side.Label, side.Path ?? "Text is kept in this workspace only");
            side.Format.Content = $"{TextCompareFile.EncodingName(side.Encoding)} · {(side.MixedLineEndings ? "Mixed" : side.NewLine == "\r\n" ? "CRLF" : side.NewLine == "\r" ? "CR" : "LF")}";
        }
        private void AddFileHeader(ComparisonEditor editor, FileSide file, bool isLeft)
        {
            string Text() => isLeft ? vm.Original : vm.Modified;
            var originalHeading = (TextBlock)editor.Pane.Children[0];
            editor.Pane.Children.Remove(originalHeading);
            var open = Ui.AsyncButton("Open…", async () => { var path = await dialogs.OpenAsync(); if (path is not null) await Open(path); }, ex => feedback.Text = $"Could not open text: {ex.Message}");
            var save = Ui.AsyncButton("Save as…", async () =>
            {
                await editor.FlushAsync();
                var text = Text(); var encoding = file.Encoding;
                var path = await dialogs.SaveAsync(file.Path is null ? (isLeft ? "original" : "modified") : System.IO.Path.GetFileNameWithoutExtension(file.Path), ".txt", "Text file");
                if (path is null) return;
                var bytes = TextCompareFile.Encode(text, encoding);
                await new AtomicFileWriter().WriteAsync(path, async (stream, token) => await stream.WriteAsync(bytes, token));
                file.Path = path; file.SavedText = text; file.SavedEncoding = TextCompareFile.EncodingKey(encoding); UpdateFile(file, Text()); feedback.Text = "Text saved.";
            }, ex => feedback.Text = $"Could not save text: {ex.Message}");
            var copy = Ui.AsyncButton("Copy", async () =>
            {
                await editor.FlushAsync();
                try { var data = new DataPackage(); data.SetText(Text()); Clipboard.SetContent(data); feedback.Text = "Text copied."; }
                catch (Exception ex) { feedback.Text = $"Could not copy text: {ex.Message}"; }
            });
            var formatMenu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Bottom };
            var encodings = new MenuFlyoutSubItem { Text = "Encoding for open / save" };
            foreach (var encoding in new Encoding[] { new UTF8Encoding(false, true), new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true), Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) })
            {
                var item = new MenuFlyoutItem { Text = TextCompareFile.EncodingName(encoding) };
                item.Click += (_, _) => { file.Encoding = file.ReadEncoding = encoding; UpdateFile(file, Text()); };
                encodings.Items.Add(item);
            }
            formatMenu.Items.Add(encodings);
            var endings = new MenuFlyoutSubItem { Text = "Convert line endings" };
            foreach (var (label, ending) in new[] { ("Windows · CRLF", "\r\n"), ("Unix · LF", "\n"), ("Classic Mac · CR", "\r") })
            {
                var item = new MenuFlyoutItem { Text = label };
                item.Click += (_, _) =>
                {
                    file.NewLine = ending; file.MixedLineEndings = false;
                    var text = Text().ReplaceLineEndings(ending);
                    if (isLeft) vm.Original = text; else vm.Modified = text;
                    UpdateFile(file, text); RequestCompare(true);
                };
                endings.Items.Add(item);
            }
            formatMenu.Items.Add(endings); file.Format.Flyout = formatMenu;
            ToolTipService.SetToolTip(file.Format, "Select encoding for BOM-less files and saving, or convert line endings. Files with a BOM use their declared encoding.");
            file.Label.Opacity = .65; file.Label.MaxWidth = 260; file.Label.TextTrimming = TextTrimming.CharacterEllipsis;
            editor.Pane.Children.Insert(0, ToolLayout.Stack(Ui.Row(originalHeading, file.Label), Ui.Row(open, save, copy, file.Format)));
            UpdateFile(file, Text());
            editor.AllowDrop = true;
            editor.DragOver += (_, e) => { if (e.DataView.Contains(StandardDataFormats.StorageItems)) { e.AcceptedOperation = DataPackageOperation.Copy; e.Handled = true; } };
            editor.Drop += async (_, e) =>
            {
                if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
                e.Handled = true;
                try
                {
                    var items = await e.DataView.GetStorageItemsAsync();
                    if (items.Count != 1 || items[0] is not Windows.Storage.StorageFile storage) { feedback.Text = "Drop one text file on each pane."; return; }
                    await Open(storage.Path);
                }
                catch (Exception ex) { feedback.Text = $"Could not open text: {ex.Message}"; }
            };
            async Task Open(string path)
            {
                var bytes = await File.ReadAllBytesAsync(path);
                var decoded = TextCompareFile.Decode(bytes, file.ReadEncoding);
                var text = decoded.Text;
                file.Encoding = decoded.Encoding; file.Path = path; file.SavedText = text; file.SavedEncoding = TextCompareFile.EncodingKey(decoded.Encoding);
                file.NewLine = decoded.NewLine; file.MixedLineEndings = decoded.MixedLineEndings;
                if (isLeft) vm.Original = text; else vm.Modified = text;
                UpdateFile(file, text); feedback.Text = decoded.MixedLineEndings ? "Mixed line endings retained. Editing uses the first line-ending convention." : ""; RequestCompare(true);
            }
        }
    }
}
