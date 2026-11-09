using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Text;
namespace ProtoBench.Desktop.Controls;

public static class Ui
{
    public static TextBlock Text(string text, double size = 14)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        ToolLayout.HideWhenEmpty(block);
        return block;
    }
    public static TextBlock Heading(string text) => new() { Text = text, FontSize = 28, FontWeight = FontWeights.SemiBold };
    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => action();
        return button;
    }
    public static Button AsyncButton(string text, Func<Task> action, Action<Exception>? error = null)
    {
        var button = new Button { Content = text };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (Exception ex) { error?.Invoke(ex); } finally { button.IsEnabled = true; } };
        return button;
    }
    public static StackPanel Row(params UIElement[] elements)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var e in elements)
            panel.Children.Add(e);
        return panel;
    }
    public static StackPanel Column(params UIElement[] elements)
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(28), MaxWidth = 1200, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var e in elements)
            panel.Children.Add(e);
        return panel;
    }
    public static TextBox Input(object source, string property, string header, bool multiline = false)
    {
        var box = new TextBox { Header = header, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 80 : 32, MaxHeight = multiline ? 240 : double.PositiveInfinity };
        box.SetBinding(TextBox.TextProperty, new Binding { Source = source, Path = new PropertyPath(property), Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        return box;
    }
    public static TextBlock BoundText(object source, string property)
    {
        var block = Text("");
        block.SetBinding(TextBlock.TextProperty, new Binding { Source = source, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
        return block;
    }
    public static ProgressRing Progress(object source)
    {
        var ring = new ProgressRing { Width = 24, Height = 24 };
        ring.Visibility = Visibility.Collapsed;
        ring.RegisterPropertyChangedCallback(ProgressRing.IsActiveProperty, (_, _) => ring.Visibility = ring.IsActive ? Visibility.Visible : Visibility.Collapsed);
        ring.SetBinding(ProgressRing.IsActiveProperty, new Binding { Source = source, Path = new PropertyPath("IsBusy"), Mode = BindingMode.OneWay });
        return ring;
    }
}
