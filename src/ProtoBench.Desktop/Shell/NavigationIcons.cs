using ProtoBench.Application.Tools;

namespace ProtoBench.Desktop.Shell;

/// <summary>Distinct system glyphs for built-in tools and navigation categories.</summary>
internal static class NavigationIcons
{
    public static Microsoft.UI.Xaml.Controls.StackPanel CustomBadge(bool showLabel = true)
    {
        var badge = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 4 };
        badge.Children.Add(new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "\uEA86", FontSize = 12 });
        if (showLabel) badge.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Custom tool", FontSize = 12 });
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(badge, "Custom tool · Provided by a developer plugin");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(badge, "Custom tool provided by a developer plugin");
        return badge;
    }

    public static object ToolLabel(ToolDescriptor tool)
    {
        if (!tool.IsCustom) return tool.Name;
        var label = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 8 };
        label.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition());
        label.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
        label.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock { Text = tool.Name, TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis });
        var badge = CustomBadge(false);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(badge, 1);
        label.Children.Add(badge);
        return label;
    }

    // Segoe Fluent / MDL2 names and code points:
    // https://learn.microsoft.com/windows/apps/design/iconography/segoe-fluent-icons-font
    public const string BaseConverter = "\uE8B1"; // Shuffle
    public const string LogParser = "\uE721"; // Search
    public const string MessageCompare = "\uE8AB"; // Switch
    public const string FileGenerator = "\uE710"; // Add
    public const string EmvDecoder = "\uE8EC"; // Tag
    public const string IsoBitmap = "\uE8A9"; // ViewAll
    public const string Luhn = "\uE8FB"; // Accept
    public const string Arqc = "\uE8D7"; // Permissions
    public const string Bitwise = "\uE943"; // Code
    public const string Base64 = "\uE8C1"; // Characters
    public const string TripleDes = "\uE72E"; // Lock
    public const string Certificate = "\uEB95"; // Certificate
    public const string NetworkRelay = "\uE71B"; // Link
    public const string TelnetServer = "\uE756"; // CommandPrompt
    public const string TimedKeyPresser = "\uE765"; // KeyboardClassic
    public const string TextCleanup = "\uE894"; // Clear
    public const string TextCompare = "\uE89F"; // TwoPage

    public static string Category(ToolCategory category) => category switch
    {
        ToolCategory.Parsers => "\uE8A5", // Document
        ToolCategory.Converters => "\uE8AB", // Switch
        ToolCategory.Generators => "\uE74E", // Save
        ToolCategory.Comparisons => "\uE89F", // TwoPage
        ToolCategory.Utilities => "\uE90F", // Repair
        ToolCategory.Payments => "\uE8C7", // PaymentCard
        ToolCategory.Calculators => "\uE8EF", // Calculator
        _ => "\uEA86" // Puzzle: custom/plugin tools
    };
}
