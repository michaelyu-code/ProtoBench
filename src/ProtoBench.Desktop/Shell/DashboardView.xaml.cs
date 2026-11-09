using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Application.Tools;
using ProtoBench.Desktop.Controls;

namespace ProtoBench.Desktop.Shell;

public sealed partial class DashboardView : UserControl
{
    private readonly ShellViewModel vm;
    private readonly Action<string, bool> open;
    private readonly Func<string, Task> openFile;
    private readonly Action<Exception> report;
    private readonly List<FrameworkElement> cards = [];
    private int columns;

    public DashboardView(ShellViewModel vm, Action<string, bool> open, Func<string, Task> openFile, Action<Exception> report)
    {
        this.vm = vm;
        this.open = open;
        this.openFile = openFile;
        this.report = report;
        InitializeComponent();
        Category.ItemsSource = new[] { "All tools", "Favorites" }
            .Concat(vm.Tools.Tools.Select(t => t.Category.DisplayName()).Distinct().Order()).ToArray();
        Category.SelectedIndex = 0;
        Search.TextChanged += (_, _) => { vm.Query = Search.Text; RefreshTools(); };
        Category.SelectionChanged += (_, _) => RefreshTools();
        ResetFilters.Click += (_, _) => { Search.Text = ""; Category.SelectedIndex = 0; };
        ToolCards.SizeChanged += (_, _) => ReflowCards();
        Filters.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 520;
            Grid.SetRow(Category, narrow ? 1 : 0);
            Grid.SetColumn(Category, narrow ? 0 : 1);
            Grid.SetColumnSpan(Search, narrow ? 2 : 1);
            Grid.SetColumnSpan(Category, narrow ? 2 : 1);
        };
        Refresh();
    }

    public void ShowCategory(ToolCategory? category)
    {
        Search.Text = "";
        Category.SelectedItem = category?.DisplayName() ?? "All tools";
        RefreshTools();
    }

    public void Refresh()
    {
        RefreshTools();
        RecentFiles.Children.Clear();
        foreach (var path in vm.Settings.Current.RecentFiles)
        {
            var button = Ui.AsyncButton(Path.GetFileName(path), () => openFile(path), report);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            ToolTipService.SetToolTip(button, path);
            RecentFiles.Children.Add(button);
        }
        if (RecentFiles.Children.Count == 0)
            RecentFiles.Children.Add(Ui.Text("Files you open or save will appear here."));
    }

    private void RefreshTools()
    {
        ToolCards.Children.Clear();
        cards.Clear();
        var favorites = vm.Settings.Current.FavoriteTools;
        var filter = Category.SelectedItem as string ?? "All tools";
        var tools = vm.FilteredTools
            .Where(t => filter == "All tools" || (filter == "Favorites" ? favorites.Contains(t.Id) : t.Category.DisplayName() == filter))
            .OrderByDescending(t => favorites.Contains(t.Id)).ThenBy(t => t.Name).ToArray();
        ToolCount.Text = $"{tools.Length} {(tools.Length == 1 ? "tool" : "tools")}";
        EmptyState.Visibility = tools.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var tool in tools)
        {
            var card = CreateCard(tool, favorites.Contains(tool.Id));
            cards.Add(card);
            ToolCards.Children.Add(card);
        }
        columns = 0;
        ReflowCards();
    }

    private FrameworkElement CreateCard(ToolDescriptor tool, bool favorite)
    {
        var layout = new Grid { Padding = new Thickness(16), RowSpacing = 12, MinHeight = 180 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new FontIcon { Glyph = tool.Icon, FontSize = 24, VerticalAlignment = VerticalAlignment.Center });
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock { Text = tool.Name, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        title.Children.Add(new TextBlock { Text = tool.Category.DisplayName(), FontSize = 12, Foreground = Resource("TextFillColorSecondaryBrush") });
        Grid.SetColumn(title, 1);
        header.Children.Add(title);
        var star = Ui.AsyncButton(favorite ? "★" : "☆", () => vm.Settings.ToggleFavoriteAsync(tool.Id), report);
        star.Padding = new Thickness(8);
        star.VerticalAlignment = VerticalAlignment.Top;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(star, (favorite ? "Remove favorite: " : "Favorite: ") + tool.Name);
        ToolTipService.SetToolTip(star, favorite ? "Remove from favorites" : "Add to favorites");
        Grid.SetColumn(star, 2);
        header.Children.Add(star);
        layout.Children.Add(header);
        var description = Ui.Text(tool.Description);
        description.Foreground = Resource("TextFillColorSecondaryBrush");
        Grid.SetRow(description, 1);
        layout.Children.Add(description);
        var primary = Ui.Button("Open", () => open(tool.Id, false));
        primary.Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["AccentButtonStyle"];
        var actions = Ui.Row(primary, Ui.Button("New tab", () => open(tool.Id, true)));
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);
        return new Border
        {
            Child = layout,
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Resource("CardStrokeColorDefaultBrush"),
            Background = Resource("CardBackgroundFillColorDefaultBrush")
        };
    }

    private void ReflowCards()
    {
        var next = Math.Clamp((int)((ToolCards.ActualWidth + 16) / 320), 1, 3);
        if (columns == next)
            return;
        columns = next;
        ToolCards.ColumnDefinitions.Clear();
        ToolCards.RowDefinitions.Clear();
        for (var i = 0; i < columns; i++)
            ToolCards.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < (cards.Count + columns - 1) / columns; i++)
            ToolCards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cards.Count; i++)
        {
            Grid.SetRow(cards[i], i / columns);
            Grid.SetColumn(cards[i], i % columns);
        }
    }

    private static Brush Resource(string key) => (Brush)Microsoft.UI.Xaml.Application.Current.Resources[key];
}
