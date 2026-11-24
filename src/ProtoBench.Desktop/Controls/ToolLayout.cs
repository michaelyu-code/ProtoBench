using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Application.Workspaces;

namespace ProtoBench.Desktop.Controls;

/// <summary>Reflows visible children into equal-width columns without changing their state or focus.</summary>
public sealed class AdaptiveColumns : Grid
{
    public int MaximumColumns { get; set; } = 2;
    public double MinimumColumnWidth { get; set; } = 340;
    private readonly HashSet<UIElement> observed = [];
    private int columns = -1, visibleCount = -1;

    public AdaptiveColumns()
    {
        ColumnSpacing = RowSpacing = 12;
        SizeChanged += (_, _) => Reflow();
        Loaded += (_, _) =>
        {
            foreach (var child in Children)
                if (observed.Add(child)) child.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => Reflow());
            Reflow();
        };
    }
    private void Reflow()
    {
        var visible = Children.OfType<FrameworkElement>().Where(c => c.Visibility == Visibility.Visible).ToArray();
        var count = Math.Max(1, Math.Min(Math.Min(MaximumColumns, visible.Length), (int)((ActualWidth + ColumnSpacing) / (MinimumColumnWidth + ColumnSpacing))));
        if (columns != count || visibleCount != visible.Length)
        {
            columns = count;
            visibleCount = visible.Length;
            ColumnDefinitions.Clear();
            RowDefinitions.Clear();
            for (var i = 0; i < count; i++) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var i = 0; i < Math.Max(1, (visible.Length + count - 1) / count); i++) RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }
        for (var i = 0; i < visible.Length; i++) { SetColumn(visible[i], i % count); SetRow(visible[i], i / count); }
    }
    public void RefreshLayout() => Reflow();
}

public static class ToolLayout
{
    private sealed class WorkspaceGrid : Grid;
    private sealed class ResultCard : Grid;

    /// <summary>Preserves the same editors while switching narrow workspaces between input and output.</summary>
    public static FrameworkElement Workspace(ToolWorkspace source, FrameworkElement input, FrameworkElement output,
        Func<bool> hasResult, string resultProperty = "Output", double resultColumnWeight = 1)
    {
        var host = new WorkspaceGrid { ColumnSpacing = 12, RowSpacing = 8 };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition());
        host.ColumnDefinitions.Add(new ColumnDefinition());
        var edit = new ToggleButton { Content = "Inputs", IsChecked = true };
        var inspect = new ToggleButton { Content = "Results" };
        var navigation = Ui.Row(edit, inspect);
        var resultStatus = new ContentControl { Content = Ui.BoundText(source, nameof(source.Status)), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(resultStatus, 2); Grid.SetColumnSpan(resultStatus, 2);
        Grid.SetColumnSpan(navigation, 2);
        FrameworkElement inputScroll = input is ResultCard ? input : Scroll(input);
        FrameworkElement outputScroll = output is ResultCard ? output : Scroll(output);
        host.Children.Add(navigation); host.Children.Add(inputScroll); host.Children.Add(outputScroll);
        host.Children.Add(resultStatus);
        Grid.SetRow(inputScroll, 1); Grid.SetRow(outputScroll, 1);
        bool results = false, narrow = false, pending = false;
        void Refresh()
        {
            narrow = host.ActualWidth < 692;
            navigation.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            host.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(resultColumnWeight, GridUnitType.Star);
            Grid.SetColumn(outputScroll, narrow ? 0 : 1);
            inputScroll.Visibility = !narrow || !results ? Visibility.Visible : Visibility.Collapsed;
            outputScroll.Visibility = !narrow || results ? Visibility.Visible : Visibility.Collapsed;
            resultStatus.Visibility = narrow && results ? Visibility.Visible : Visibility.Collapsed;
            edit.IsChecked = !results; inspect.IsChecked = results;
        }
        edit.Click += (_, _) => { results = false; Refresh(); };
        inspect.Click += (_, _) => { results = true; Refresh(); };
        host.SizeChanged += (_, _) => Refresh();
        source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(source.IsBusy) && !source.IsBusy && hasResult()) pending = true;
            if (args.PropertyName == resultProperty)
            {
                pending = hasResult();
                if (!pending) { results = false; Refresh(); }
            }
            if (pending && !source.IsBusy && host.IsLoaded)
            {
                pending = false; results = true;
                if (narrow) inspect.Focus(FocusState.Programmatic);
                Refresh();
            }
        };
        host.Loaded += (_, _) => Refresh();
        return host;
    }
    public static StackPanel Stack(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static AdaptiveColumns Columns(double minimumWidth, params FrameworkElement[] children)
    {
        var grid = new AdaptiveColumns { MinimumColumnWidth = minimumWidth };
        foreach (var child in children) { child.VerticalAlignment = VerticalAlignment.Top; grid.Children.Add(child); }
        return grid;
    }
    public static FrameworkElement Section(string title, params UIElement[] children)
    {
        var border = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Background="{ThemeResource LayerFillColorDefaultBrush}"
                    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="8" Padding="16" />
            """);
        var panel = Stack();
        if (title.Length > 0) panel.Children.Add(Ui.Text(title, 18));
        foreach (var child in children) panel.Children.Add(child);
        border.Child = panel;
        border.SizeChanged += (_, _) => border.Padding = new Thickness(border.ActualWidth < 480 ? 10 : 16);
        return border;
    }
    public static Expander Help(FrameworkElement text) => new()
    {
        Header = "Help & examples", Content = Scroll(text), IsExpanded = false,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
    };

    /// <summary>A bounded result editor that uses the available pane height.</summary>
    public static FrameworkElement TextResult(string title, RawTextViewer viewer, Button copy, TextBlock feedback, Button? secondaryCopy = null,
        FrameworkElement? aboveResult = null)
    {
        viewer.Header = null;
        viewer.CompactViewport = false;
        viewer.MinHeight = 0;
        viewer.MaxHeight = double.PositiveInfinity;
        viewer.VerticalAlignment = VerticalAlignment.Stretch;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(viewer, title);
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Ui.Text(title, 18);
        label.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(label);
        Grid.SetColumn(copy, 1); header.Children.Add(copy);
        var layout = new Grid { RowSpacing = 10 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(header);
        if (aboveResult is not null) { Grid.SetRow(aboveResult, 1); layout.Children.Add(aboveResult); }
        Grid.SetRow(viewer, 2); layout.Children.Add(viewer);
        var footer = new StackPanel { Spacing = 8 };
        if (secondaryCopy is not null) footer.Children.Add(secondaryCopy);
        footer.Children.Add(feedback);
        Grid.SetRow(footer, 3); layout.Children.Add(footer);
        var style = (Border)Section("");
        style.Child = layout;
        var card = new ResultCard();
        card.Children.Add(style);
        return card;
    }

    /// <summary>Fits a grid to its pane, with an outer scroll only below its minimum usable height.</summary>
    public static FrameworkElement FittedSection(string title, Grid content)
    {
        var scroll = Scroll(content);
        void Resize() => content.Height = Math.Max(content.MinHeight, scroll.ActualHeight);
        scroll.SizeChanged += (_, _) => Resize();
        content.RegisterPropertyChangedCallback(FrameworkElement.MinHeightProperty, (_, _) => Resize());
        var layout = new Grid { RowSpacing = 10 };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(Ui.Text(title, 18));
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var border = (Border)Section("");
        border.Child = layout;
        var card = new ResultCard();
        card.Children.Add(border);
        return card;
    }
    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };

    /// <summary>Uses the available page height, leaving scrolling to the content's own editors.</summary>
    public static Grid FittedPage(string title, string subtitle, FrameworkElement content, params UIElement[] footer)
    {
        var host = new WorkspaceGrid();
        host.Children.Add(content);
        return Page(title, subtitle, [host, .. footer]);
    }

    public static Grid Page(string title, string subtitle, params UIElement[] children)
    {
        var page = new Grid { Margin = new Thickness(12), RowSpacing = 8 };
        var heading = Ui.Heading(title);
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.Children.Add(heading);
        var firstContentRow = 1;
        if (!string.IsNullOrEmpty(subtitle))
        {
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var description = Ui.Text(subtitle);
            Grid.SetRow(description, 1); page.Children.Add(description);
            firstContentRow = 2;
        }
        for (var i = 0; i < children.Length; i++)
        {
            page.RowDefinitions.Add(new RowDefinition { Height = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            var child = i == 0 && children[i] is not WorkspaceGrid ? Scroll(children[i]) : children[i];
            if (child is FrameworkElement element) Grid.SetRow(element, i + firstContentRow);
            page.Children.Add(child);
        }
        page.SizeChanged += (_, _) =>
        {
            var compact = page.ActualWidth < 720 || page.ActualHeight < 640;
            page.RowSpacing = compact ? 8 : 12;
            heading.FontSize = compact ? 22 : 28;
            foreach (var help in children.OfType<Expander>())
                if (help.Content is FrameworkElement content) content.MaxHeight = Math.Max(60, page.ActualHeight * .25);
        };
        page.Loaded += (_, _) =>
        {
            for (DependencyObject? parent = VisualTreeHelper.GetParent(page); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ContentPresenter { Name: "TabContentPresenter" } presenter) continue;
                presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
                presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                break;
            }
        };
        return page;
    }
    public static void HideWhenEmpty(TextBlock text)
    {
        void Refresh() => text.Visibility = string.IsNullOrEmpty(text.Text) ? Visibility.Collapsed : Visibility.Visible;
        text.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Refresh());
        Refresh();
    }
}
