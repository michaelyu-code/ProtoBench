using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ProtoBench.Desktop.Controls;

/// <summary>Places shared tab resources in the item scope so WinUI's template states use the app's overrides.</summary>
public static class TabAppearance
{
    public static readonly DependencyProperty UseDistinctBordersProperty = DependencyProperty.RegisterAttached(
        "UseDistinctBorders", typeof(bool), typeof(TabAppearance), new PropertyMetadata(false, Apply));

    public static bool GetUseDistinctBorders(DependencyObject target) => (bool)target.GetValue(UseDistinctBordersProperty);
    public static void SetUseDistinctBorders(DependencyObject target, bool value) => target.SetValue(UseDistinctBordersProperty, value);

    private static void Apply(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not TabViewItem item || args.NewValue is not true) return;
        var shared = Microsoft.UI.Xaml.Application.Current.Resources;
        var scoped = new ResourceDictionary();
        foreach (var theme in shared.ThemeDictionaries)
        {
            if (theme.Value is not ResourceDictionary source) continue;
            var resources = new ResourceDictionary();
            foreach (var resource in source)
                if (resource.Key is string key && key.StartsWith("TabView", StringComparison.Ordinal))
                    resources[key] = resource.Value;
            scoped.ThemeDictionaries[theme.Key] = resources;
        }
        foreach (var key in new[] { "TabViewItemBorderThickness", "TabViewSelectedItemBorderThickness",
                     "TabViewSelectedItemHeaderMargin", "TabViewItemSeparatorMargin" })
            scoped[key] = shared[key];
        item.Resources.MergedDictionaries.Add(scoped);
    }
}
