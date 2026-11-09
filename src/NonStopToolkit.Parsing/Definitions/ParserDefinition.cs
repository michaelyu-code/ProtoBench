using System.Text.Json;
using System.Text.RegularExpressions;
using NonStopToolkit.Core.Encoding;
namespace NonStopToolkit.Parsing.Definitions;

public sealed record FieldDefinition
{
    public string Name { get; init; } = "";
    public int Start
    {
        get; init;
    }
    public int Length
    {
        get; init;
    }
    public string Transform { get; init; } = "none";
}
public sealed record ParserDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Strategy { get; init; } = "delimited";
    public string Encoding { get; init; } = "utf-8";
    public string DetectionPrefix { get; init; } = "";
    public int DetectionPriority
    {
        get; init;
    }
    public string Delimiter { get; init; } = "|";
    public string? Pattern
    {
        get; init;
    }
    public List<FieldDefinition> Fields { get; init; } = [];
    public static ParserDefinition FromJson(string json)
    {
        var definition = JsonSerializer.Deserialize<ParserDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new ArgumentException("Empty parser definition.");
        definition.Validate();
        return definition;
    }
    public void Validate()
    {
        if (SchemaVersion != 1)
            throw new ArgumentException("Unsupported parser schema version.");
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Parser ID and name are required.");
        _ = StrictEncoding.Get(Encoding);
        if (Strategy is not ("delimited" or "fixed-width" or "regex"))
            throw new ArgumentException("Unknown parsing strategy.");
        if (Fields is null || Fields.Count is 0 or > 256 || Fields.Any(f => f is null || string.IsNullOrWhiteSpace(f.Name)) || Fields.Select(f => f.Name).Distinct().Count() != Fields.Count)
            throw new ArgumentException("Supply 1–256 uniquely named fields.");
        if (Fields.Any(f => f.Transform is not ("none" or "trim" or "upper" or "lower")))
            throw new ArgumentException("Unknown field transformation.");
        if (Strategy == "delimited" && (string.IsNullOrEmpty(Delimiter) || Delimiter.Length > 8))
            throw new ArgumentException("Delimiter must contain 1–8 characters.");
        if (Strategy == "fixed-width" && Fields.Any(f => f.Start < 0 || f.Length <= 0 || (long)f.Start + f.Length > 65536))
            throw new ArgumentException("Invalid fixed-width field bounds.");
        if (Strategy == "regex")
        {
            if (string.IsNullOrEmpty(Pattern) || Pattern.Length > 4096)
                throw new ArgumentException("Regex pattern is required and limited to 4096 characters.");
            var regex = new Regex(Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (Fields.Any(f => !regex.GetGroupNames().Contains(f.Name)))
                throw new ArgumentException("Each regex field must have a named capture group.");
        }
    }
}
