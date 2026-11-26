namespace ProtoBench.Application.Tools;

public enum ToolCategory
{
    Parsers, Converters, Generators, Comparisons, Utilities, Payments, Calculators, CustomTools
}
public static class ToolCategoryNames
{
    public static string DisplayName(this ToolCategory category) => category == ToolCategory.CustomTools ? "Custom Tools" : category.ToString();
}
public sealed record ToolDescriptor(string Id, string Name, string Description, ToolCategory Category, string Icon)
{
    public bool IsCustom { get; init; }
    public string CustomLabel => IsCustom ? "Custom tool" : "";
    public string CustomIcon => IsCustom ? "\uEA86" : "";
    public string CategoryName => Category.DisplayName();

    public static ToolDescriptor FromPlugin(ProtoBench.PluginSdk.PluginTool tool) =>
        new(tool.Id, tool.Name, tool.Description, tool.Category switch
        {
            ProtoBench.PluginSdk.PluginToolCategory.CustomTools => ToolCategory.CustomTools,
            ProtoBench.PluginSdk.PluginToolCategory.Parsers => ToolCategory.Parsers,
            ProtoBench.PluginSdk.PluginToolCategory.Converters => ToolCategory.Converters,
            ProtoBench.PluginSdk.PluginToolCategory.Generators => ToolCategory.Generators,
            ProtoBench.PluginSdk.PluginToolCategory.Comparisons => ToolCategory.Comparisons,
            ProtoBench.PluginSdk.PluginToolCategory.Utilities => ToolCategory.Utilities,
            ProtoBench.PluginSdk.PluginToolCategory.Payments => ToolCategory.Payments,
            ProtoBench.PluginSdk.PluginToolCategory.Calculators => ToolCategory.Calculators,
            _ => ToolCategory.CustomTools
        }, tool.Icon) { IsCustom = true };
}
public interface IToolRegistry
{
    IReadOnlyList<ToolDescriptor> Tools
    {
        get;
    }
    ToolDescriptor Get(string id);
}
public sealed class ToolRegistry : IToolRegistry
{
    public IReadOnlyList<ToolDescriptor> Tools
    {
        get;
    }
    public ToolRegistry(IEnumerable<ToolDescriptor> tools)
    {
        var list = tools.ToArray();
        if (list.Any(t => string.IsNullOrWhiteSpace(t.Id)))
            throw new ArgumentException("Tool IDs must be nonempty.");
        if (list.Select(t => t.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length)
            throw new ArgumentException("Duplicate tool ID.");
        Tools = Array.AsReadOnly(list);
    }
    public ToolDescriptor Get(string id) => Tools.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"Unknown tool '{id}'.");
}
