using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Controls.Primitives;
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
}

public static class ToolLayout
{
    /// <summary>Preserves the same editors while switching narrow workspaces between input and output.</summary>
    public static FrameworkElement Workspace(ToolWorkspace source, FrameworkElement input, FrameworkElement output,
        Func<bool> hasResult, string resultProperty = "Output")
    {
        var host = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        host.ColumnDefinitions.Add(new ColumnDefinition());
        host.ColumnDefinitions.Add(new ColumnDefinition());
        var edit = new ToggleButton { Content = "Inputs", IsChecked = true };
        var inspect = new ToggleButton { Content = "Results" };
        var navigation = Ui.Row(edit, inspect);
        var resultStatus = new ContentControl { Content = Ui.BoundText(source, nameof(source.Status)), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(resultStatus, 2); Grid.SetColumnSpan(resultStatus, 2);
        Grid.SetColumnSpan(navigation, 2);
        host.Children.Add(navigation); host.Children.Add(input); host.Children.Add(output);
        host.Children.Add(resultStatus);
        Grid.SetRow(input, 1); Grid.SetRow(output, 1);
        bool results = false, narrow = false, pending = false;
        void Refresh()
        {
            narrow = host.ActualWidth < 692;
            navigation.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
            host.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(output, narrow ? 0 : 1);
            input.Visibility = !narrow || !results ? Visibility.Visible : Visibility.Collapsed;
            output.Visibility = !narrow || results ? Visibility.Visible : Visibility.Collapsed;
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
        Header = "Help & examples", Content = text, IsExpanded = false,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
    };
    public static ScrollViewer Page(string title, string subtitle, params UIElement[] children)
    {
        var panel = Stack(Ui.Heading(title), Ui.Text(subtitle));
        panel.Margin = new Thickness(20);
        panel.Spacing = 12;
        foreach (var child in children) panel.Children.Add(child);
        var page = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch };
        page.SizeChanged += (_, _) =>
        {
            var compact = page.ActualWidth < 720 || page.ActualHeight < 640;
            panel.Margin = new Thickness(compact ? 12 : 20);
            panel.Spacing = compact ? 8 : 12;
            ((TextBlock)panel.Children[0]).FontSize = compact ? 22 : 28;
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
