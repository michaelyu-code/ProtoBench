using ProtoBench.Application.Tools;

namespace ProtoBench.Desktop.Shell;

/// <summary>Distinct system glyphs for built-in tools and navigation categories.</summary>
internal static class NavigationIcons
{
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
