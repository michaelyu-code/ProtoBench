using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    public FrameworkElement Viewport => list;
    private bool updating, whitespace;
    public void SetInteractive(bool enabled) => list.IsEnabled = enabled;
    public TextCompareReview(Action<int> select)
    {
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition());
        var headers = new Grid { Margin = new Thickness(8, 0, 8, 8), ColumnSpacing = 42 };
        headers.ColumnDefinitions.Add(new ColumnDefinition()); headers.ColumnDefinitions.Add(new ColumnDefinition());
        headers.Children.Add(Ui.Text("Original · − removed"));
        var right = Ui.Text("Modified · + added"); Grid.SetColumn(right, 1); headers.Children.Add(right);
        Children.Add(headers); Grid.SetRow(list, 1); Children.Add(list);
        list.ItemContainerStyle = (Style)XamlReader.Load("""
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListViewItem">
              <Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Padding" Value="0"/>
              <Setter Property="Margin" Value="0"/><Setter Property="MinHeight" Value="24"/>
            </Style>
            """);
        list.ItemTemplate = (DataTemplate)XamlReader.Load("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Grid MinWidth="{Binding MinimumWidth}">
                <Grid.ColumnDefinitions><ColumnDefinition Width="52"/><ColumnDefinition Width="*"/><ColumnDefinition Width="42"/><ColumnDefinition Width="52"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
                <Border Grid.ColumnSpan="2" Background="{Binding LeftBackground}"/>
                <Border Grid.Column="3" Grid.ColumnSpan="2" Background="{Binding RightBackground}"/>
                <TextBlock Text="{Binding LeftNumber}" Margin="4,2,8,2" TextAlignment="Right" Foreground="{Binding NumberForeground}" FontFamily="Consolas"/>
                <TextBlock x:Name="LeftText" Grid.Column="1" Text="{Binding LeftText}" Margin="0,2,4,2" FontFamily="Consolas" FontSize="18" TextWrapping="{Binding Wrapping}" Foreground="{Binding Foreground}" IsTextSelectionEnabled="True"/>
                <TextBlock Grid.Column="2" Text="{Binding Marker}" HorizontalAlignment="Center" VerticalAlignment="Top" Margin="0,2,0,2"/>
                <TextBlock Grid.Column="3" Text="{Binding RightNumber}" Margin="4,2,8,2" TextAlignment="Right" Foreground="{Binding NumberForeground}" FontFamily="Consolas"/>
                <TextBlock x:Name="RightText" Grid.Column="4" Text="{Binding RightText}" Margin="0,2,4,2" FontFamily="Consolas" FontSize="18" TextWrapping="{Binding Wrapping}" Foreground="{Binding Foreground}" IsTextSelectionEnabled="True"/>
              </Grid>
            </DataTemplate>
            """);
        list.SelectionChanged += (_, _) => { if (!updating && list.SelectedItem is ReviewRow { ChangeIndex: >= 0 } row) select(row.ChangeIndex); };
        list.ContainerContentChanging += RenderRow;
    }

    private void RenderRow(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (!args.InRecycleQueue && args.Phase == 0) { args.RegisterUpdateCallback(1, RenderRow); return; }
            if (args.InRecycleQueue || args.Item is not ReviewRow row || !row.Row.IsChange || args.ItemContainer.ContentTemplateRoot is not Grid root) return;
            var left = (TextBlock)root.FindName("LeftText"); var rightText = (TextBlock)root.FindName("RightText");
            left.Inlines.Clear(); rightText.Inlines.Clear();
            var a = row.Row.Left?.Content ?? ""; var b = row.Row.Right?.Content ?? "";
            if (a.Length + b.Length > 4096) { left.Text = Display(a); rightText.Text = Display(b); return; }
            foreach (var part in TextComparison.Compare(a, b).Differences)
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

    public void Update(TextCompareDocument document, HashSet<int> visible, int selected, bool wrap, bool showWhitespace)
    {
        whitespace = showWhitespace;
        var width = wrap ? 0 : Math.Max(0, visible.Count == 0 ? 0 : visible.Max(i => Math.Max(document.Rows[i].Left?.Content.Length ?? 0, document.Rows[i].Right?.Content.Length ?? 0)) * 17d + 160);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, wrap ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(list, wrap ? ScrollMode.Disabled : ScrollMode.Enabled);
        updating = true;
        try { list.ItemsSource = visible.Order().Select(i => new ReviewRow(document.Rows[i], Display(document.Rows[i].Left?.Content ?? ""), Display(document.Rows[i].Right?.Content ?? ""), wrap, width)).ToArray(); }
        finally { updating = false; }
        SelectChange(selected, false);
    }
    public void SelectChange(int change, bool reveal)
    {
        updating = true;
        try
        {
            var item = list.Items.OfType<ReviewRow>().FirstOrDefault(r => r.ChangeIndex == change && change >= 0);
            list.SelectedItem = item;
            if (reveal && item is not null) list.ScrollIntoView(item);
        }
        finally { updating = false; }
    }
    private string Display(string text) => whitespace ? text.Replace(" ", "·").Replace("\t", "→   ") : text;
    public sealed class ReviewRow(ComparisonRow row, string leftText, string rightText, bool wrap, double width)
    {
        public ComparisonRow Row { get; } = row;
        public int ChangeIndex => Row.ChangeIndex;
        public string LeftNumber => Row.Left?.Number.ToString() ?? "—";
        public string RightNumber => Row.Right?.Number.ToString() ?? "—";
        public string LeftText { get; } = leftText;
        public string RightText { get; } = rightText;
        public string Marker => Row.IsChange ? Row.Left is null ? "+" : Row.Right is null ? "−" : "≠" : "";
        public TextWrapping Wrapping => wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        public double MinimumWidth => width;
        public override string ToString() => $"Original line {LeftNumber}, modified line {RightNumber}{(Row.IsChange ? ", changed" : ", context")}";
        public Brush Foreground => Row.IsChange ? new SolidColorBrush(Colors.Black) : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorPrimaryBrush"];
        public Brush NumberForeground => Row.IsChange ? new SolidColorBrush(Colors.DimGray) : (Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"];
        public Brush LeftBackground => new SolidColorBrush(Row.IsChange && Row.Left is not null ? Windows.UI.Color.FromArgb(255, 255, 237, 237) : Colors.Transparent);
        public Brush RightBackground => new SolidColorBrush(Row.IsChange && Row.Right is not null ? Windows.UI.Color.FromArgb(255, 231, 247, 234) : Colors.Transparent);
    }
}
