using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Desktop.Controls;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Text;

/// <summary>Virtualized filtered review; source editors and source ranges remain untouched.</summary>
internal sealed class TextCompareReview : Grid
{
    private readonly ListView list = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly ScrollBar horizontal = new() { Orientation = Orientation.Horizontal, Height = 16, SmallChange = 32,
        IndicatorMode = ScrollingIndicatorMode.MouseIndicator, Visibility = Visibility.Collapsed };
    private readonly HashSet<Grid> realized = [];
    private bool wrapping;
    private double widestText;
    private double textFontSize = 12;
    private ReviewRow[] displayedRows = [];
    private double[] rowHeights = [];
    private bool overviewPending;
    private ScrollViewer? verticalViewport;
    public double OverviewInset { get; private set; }
    public event Action? OverviewChanged;
    public event Action<bool, int>? LineClicked;
    private ComparisonRow? highlightedLine;
    public void HighlightLine(ComparisonRow? row)
    {
        highlightedLine = row;
        foreach (var root in realized) UpdateLineHighlight(root);
    }
    private void UpdateLineHighlight(Grid root)
    {
        if (root.FindName("LineHighlight") is Border highlight)
            highlight.Visibility = highlightedLine is not null && root.Tag is RowPresentation item && item.Row.Row == highlightedLine
                ? Visibility.Visible : Visibility.Collapsed;
    }
    private void LineNumberTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is not Grid { Tag: RowPresentation item } || e.OriginalSource is not TextBlock number) return;
        var left = number.Name == "LeftNumber";
        if (!left && number.Name != "RightNumber") return;
        if ((left ? item.Row.Row.Left : item.Row.Row.Right) is not { } line) return;
        LineClicked?.Invoke(left, line.Number); e.Handled = true;
    }
    public IReadOnlyList<(int ChangeIndex, double Height)> OverviewRows =>
        displayedRows.Select((row, i) => (row.ChangeIndex, rowHeights[i])).ToArray();
    public FrameworkElement Viewport => list;
    private bool updating, whitespace;
    public void SetInteractive(bool enabled) => list.IsEnabled = enabled;
    public void RefreshOverviewLayout()
    {
        if (!list.IsLoaded) return;
        var first = realized.FirstOrDefault(root => root.Tag is RowPresentation { Row.DisplayIndex: 0 });
        if (first is null) return;
        verticalViewport ??= FindViewport(list);
        var inset = Math.Max(0, first.TransformToVisual(list).TransformPoint(new Windows.Foundation.Point()).Y + (verticalViewport?.VerticalOffset ?? 0));
        if (Math.Abs(OverviewInset - inset) < .1) return;
        OverviewInset = inset; QueueOverviewRefresh();
    }
    private static ScrollViewer? FindViewport(DependencyObject parent)
    {
        if (parent is ScrollViewer viewport) return viewport;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (FindViewport(VisualTreeHelper.GetChild(parent, i)) is { } found) return found;
        return null;
    }
    public TextCompareReview(Action<int> select)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var headers = new Grid { Margin = new Thickness(8, 0, 8, 8), ColumnSpacing = 42 };
        headers.ColumnDefinitions.Add(new ColumnDefinition()); headers.ColumnDefinitions.Add(new ColumnDefinition());
        headers.Children.Add(Ui.Text("Original · − removed"));
        var right = Ui.Text("Modified · + added"); Grid.SetColumn(right, 1); headers.Children.Add(right);
        Children.Add(headers); Grid.SetRow(list, 1); Children.Add(list);
        Grid.SetRow(horizontal, 2); Children.Add(horizontal);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(horizontal, "Scroll both comparison panes horizontally");
        horizontal.ValueChanged += (_, _) =>
        {
            foreach (var root in realized)
                if (root.Tag is RowPresentation row) PositionText(row);
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
        list.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Padding" Value="0"/>
              <Setter Property="Margin" Value="0"/><Setter Property="MinHeight" Value="24"/>
            </Style>
            """);
        list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid>
                <Grid.ColumnDefinitions><ColumnDefinition Width="{Binding NumberWidth}"/><ColumnDefinition Width="*"/><ColumnDefinition Width="42"/><ColumnDefinition Width="{Binding NumberWidth}"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Border Grid.ColumnSpan="2" Background="{Binding LeftBackground}"/>
                <Border Grid.Column="3" Grid.ColumnSpan="2" Background="{Binding RightBackground}"/>
                <TextBlock x:Name="LeftNumber" Text="{Binding LeftNumber}" Margin="4,2,8,2" TextAlignment="Right" Foreground="{Binding NumberForeground}" FontFamily="Consolas" FontSize="{Binding FontSize}"/>
                <Grid x:Name="LeftCell" Grid.Column="1">
                  <Canvas x:Name="LeftCanvas" Height="26" VerticalAlignment="Top">
                    <TextBlock x:Name="LeftText" Text="{Binding LeftText}" Margin="0,2,4,2" FontFamily="Consolas" FontSize="{Binding FontSize}" TextWrapping="{Binding Wrapping}" Foreground="{Binding Foreground}" IsTextSelectionEnabled="True"/>
                  </Canvas>
                </Grid>
                <TextBlock Grid.Column="2" Text="{Binding Marker}" HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,2,0,2"/>
                <TextBlock x:Name="RightNumber" Grid.Column="3" Text="{Binding RightNumber}" Margin="4,2,8,2" TextAlignment="Right" Foreground="{Binding NumberForeground}" FontFamily="Consolas" FontSize="{Binding FontSize}"/>
                <Grid x:Name="RightCell" Grid.Column="4">
                  <Canvas x:Name="RightCanvas" Height="26" VerticalAlignment="Top">
                    <TextBlock x:Name="RightText" Text="{Binding RightText}" Margin="0,2,4,2" FontFamily="Consolas" FontSize="{Binding FontSize}" TextWrapping="{Binding Wrapping}" Foreground="{Binding Foreground}" IsTextSelectionEnabled="True"/>
                  </Canvas>
                </Grid>
                <Border x:Name="LineHighlight" Grid.ColumnSpan="5" Visibility="Collapsed" IsHitTestVisible="False" BorderBrush="#60B8FF" BorderThickness="0,1,0,1" Background="#2440A0FF"/>
              </Grid>
            </DataTemplate>
            """);
        list.SelectionChanged += (_, _) => { if (!updating && list.SelectedItem is ReviewRow { ChangeIndex: >= 0 } row) select(row.ChangeIndex); };
        list.ContainerContentChanging += RenderRow;
    }

    private void RenderRow(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.ItemContainer.ContentTemplateRoot is Grid recycled)
            {
                realized.Remove(recycled);
                recycled.Tapped -= LineNumberTapped;
                recycled.SizeChanged -= RowSizeChanged;
                if (recycled.Tag is RowPresentation old)
                {
                    old.LeftCell.SizeChanged -= CellSizeChanged; old.RightCell.SizeChanged -= CellSizeChanged;
                    old.Left.SizeChanged -= CellSizeChanged; old.Right.SizeChanged -= CellSizeChanged;
                }
                recycled.Tag = null;
            }
            return;
        }
        if (args.Phase == 0) { args.RegisterUpdateCallback(1, RenderRow); return; }
        if (args.Item is not ReviewRow row || args.ItemContainer.ContentTemplateRoot is not Grid root) return;
        var left = (TextBlock)root.FindName("LeftText"); var rightText = (TextBlock)root.FindName("RightText");
        if (row.Row.IsChange)
        {
            left.Inlines.Clear(); rightText.Inlines.Clear();
            var a = row.Row.Left?.Content ?? ""; var b = row.Row.Right?.Content ?? "";
            if (a.Length + b.Length > 4096) { left.Text = Display(a); rightText.Text = Display(b); }
            else foreach (var part in TextComparison.Compare(a, b).Differences)
            {
                if (part.Kind != TextDifferenceKind.Added) Add(left, part, TextDifferenceKind.Removed);
                if (part.Kind != TextDifferenceKind.Removed) Add(rightText, part, TextDifferenceKind.Added);
            }
            void Add(TextBlock target, TextDifference part, TextDifferenceKind kind) => target.Inlines.Add(new Run
            {
                Text = Display(part.Text), FontWeight = part.Kind == kind ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(part.Kind == kind ? kind == TextDifferenceKind.Removed ? Colors.DarkRed : Colors.DarkGreen : Colors.Black)
            });
        }
        var presentation = new RowPresentation(row, (Grid)root.FindName("LeftCell"), (Canvas)root.FindName("LeftCanvas"), left,
            (Grid)root.FindName("RightCell"), (Canvas)root.FindName("RightCanvas"), rightText,
            Measure(row.LeftText), Measure(row.RightText));
        root.Tag = presentation; realized.Add(root);
        root.Tapped -= LineNumberTapped; root.Tapped += LineNumberTapped;
        UpdateLineHighlight(root);
        root.SizeChanged -= RowSizeChanged; root.SizeChanged += RowSizeChanged;
        presentation.LeftCell.SizeChanged -= CellSizeChanged; presentation.LeftCell.SizeChanged += CellSizeChanged;
        presentation.RightCell.SizeChanged -= CellSizeChanged; presentation.RightCell.SizeChanged += CellSizeChanged;
        left.SizeChanged -= CellSizeChanged; left.SizeChanged += CellSizeChanged;
        rightText.SizeChanged -= CellSizeChanged; rightText.SizeChanged += CellSizeChanged;
        widestText = Math.Max(widestText, Math.Max(presentation.LeftWidth, presentation.RightWidth));
        LayoutRow(presentation);
        RecordRowHeight(root);
    }

    private sealed record RowPresentation(ReviewRow Row, Grid LeftCell, Canvas LeftCanvas, TextBlock Left, Grid RightCell, Canvas RightCanvas, TextBlock Right, double LeftWidth, double RightWidth);
    private void RowSizeChanged(object sender, SizeChangedEventArgs args) { if (sender is Grid root) RecordRowHeight(root); }
    private void RecordRowHeight(Grid root)
    {
        if (root.Tag is not RowPresentation row || root.ActualHeight <= 0 || row.Row.DisplayIndex >= rowHeights.Length) return;
        var index = row.Row.DisplayIndex;
        if (Math.Abs(rowHeights[index] - root.ActualHeight) < .1) return;
        rowHeights[index] = root.ActualHeight;
        QueueOverviewRefresh();
    }
    private void QueueOverviewRefresh()
    {
        if (overviewPending) return;
        overviewPending = true;
        DispatcherQueue.TryEnqueue(() => { overviewPending = false; OverviewChanged?.Invoke(); });
    }
    private double Measure(string text)
    {
        var sample = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), FontSize = textFontSize, FontWeight = FontWeights.Bold };
        sample.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(sample.DesiredSize.Width) + 4;
    }
    private void CellSizeChanged(object sender, SizeChangedEventArgs args)
    {
        for (DependencyObject? element = sender as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is Grid { Tag: RowPresentation row }) { LayoutRow(row); return; }
    }
    private void LayoutRow(RowPresentation row)
    {
        foreach (var (cell, canvas, text, width) in new[] { (row.LeftCell, row.LeftCanvas, row.Left, row.LeftWidth), (row.RightCell, row.RightCanvas, row.Right, row.RightWidth) })
        {
            cell.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, Math.Max(0, cell.ActualWidth), Math.Max(0, cell.ActualHeight)) };
            // Explicit widths prevent unwrapped text from affecting either star column's measure.
            text.Width = wrapping ? Math.Max(0, cell.ActualWidth - 4) : Math.Max(width, cell.ActualWidth - 4);
            canvas.Height = Math.Max(24, text.ActualHeight + text.Margin.Top + text.Margin.Bottom);
        }
        var viewport = Math.Max(0, Math.Min(row.LeftCell.ActualWidth, row.RightCell.ActualWidth));
        horizontal.Maximum = wrapping ? 0 : Math.Max(0, widestText - viewport);
        horizontal.ViewportSize = viewport; horizontal.LargeChange = Math.Max(32, viewport * .8);
        horizontal.Visibility = !wrapping && horizontal.Maximum > 0 ? Visibility.Visible : Visibility.Collapsed;
        PositionText(row);
    }
    private void PositionText(RowPresentation row)
    {
        row.Left.RenderTransform = new TranslateTransform { X = wrapping ? 0 : -horizontal.Value };
        row.Right.RenderTransform = new TranslateTransform { X = wrapping ? 0 : -horizontal.Value };
    }

    public void Update(TextCompareDocument document, HashSet<int> visible, int selected, bool wrap, bool showWhitespace, double fontSize)
    {
        textFontSize = fontSize;
        whitespace = showWhitespace;
        foreach (var root in realized)
        {
            root.Tapped -= LineNumberTapped;
            root.SizeChanged -= RowSizeChanged;
            if (root.Tag is RowPresentation old)
            {
                old.LeftCell.SizeChanged -= CellSizeChanged; old.RightCell.SizeChanged -= CellSizeChanged;
                old.Left.SizeChanged -= CellSizeChanged; old.Right.SizeChanged -= CellSizeChanged;
            }
            root.Tag = null;
        }
        realized.Clear();
        wrapping = wrap; widestText = 0; horizontal.Value = 0; horizontal.Maximum = 0;
        horizontal.Visibility = Visibility.Collapsed;
        var numberWidth = Math.Max(52, document.Rows.Count.ToString().Length * fontSize * .65 + 12);
        displayedRows = visible.Order().Select((i, displayIndex) => new ReviewRow(document.Rows[i], Display(document.Rows[i].Left?.Content ?? ""),
            Display(document.Rows[i].Right?.Content ?? ""), wrap, displayIndex, fontSize, numberWidth)).ToArray();
        rowHeights = Enumerable.Repeat(Math.Max(26, fontSize * 1.3 + 4), displayedRows.Length).ToArray();
        updating = true;
        try { list.ItemsSource = displayedRows; }
        finally { updating = false; }
        SelectChange(selected, false);
        QueueOverviewRefresh();
    }
    public void SelectChange(int change, bool reveal, int? displayRow = null)
    {
        updating = true;
        try
        {
            var item = displayRow is { } index && index >= 0 && index < displayedRows.Length && displayedRows[index].ChangeIndex == change
                ? displayedRows[index] : list.Items.OfType<ReviewRow>().FirstOrDefault(r => r.ChangeIndex == change && change >= 0);
            list.SelectedItem = item;
            if (reveal && item is not null) list.ScrollIntoView(item);
        }
        finally { updating = false; }
    }
    private string Display(string text) => whitespace ? text.Replace(" ", "·").Replace("\t", "→   ") : text;
    public sealed class ReviewRow(ComparisonRow row, string leftText, string rightText, bool wrap, int displayIndex, double fontSize, double numberWidth)
    {
        public double FontSize => fontSize;
        public GridLength NumberWidth => new(numberWidth);
        public ComparisonRow Row { get; } = row;
        public int ChangeIndex => Row.ChangeIndex;
        public int DisplayIndex => displayIndex;
        public string LeftNumber => Row.Left?.Number.ToString() ?? "—";
        public string RightNumber => Row.Right?.Number.ToString() ?? "—";
        public string LeftText { get; } = leftText;
        public string RightText { get; } = rightText;
        public string Marker => Row.IsChange ? Row.Left is null ? "+" : Row.Right is null ? "−" : "≠" : "";
        public TextWrapping Wrapping => wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        public override string ToString() => $"Original line {LeftNumber}, modified line {RightNumber}{(Row.IsChange ? ", changed" : ", context")}";
        public Brush Foreground => Row.IsChange ? new SolidColorBrush(Colors.Black) : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
        public Brush NumberForeground => Row.IsChange ? new SolidColorBrush(Colors.DimGray) : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"];
        public Brush LeftBackground => new SolidColorBrush(Row.IsChange && Row.Left is not null ? Windows.UI.Color.FromArgb(255, 255, 237, 237) : Colors.Transparent);
        public Brush RightBackground => new SolidColorBrush(Row.IsChange && Row.Right is not null ? Windows.UI.Color.FromArgb(255, 231, 247, 234) : Colors.Transparent);
    }
}
