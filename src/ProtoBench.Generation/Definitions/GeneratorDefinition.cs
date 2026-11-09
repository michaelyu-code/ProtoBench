using System.Text.Json;
using ProtoBench.Core.Encoding;
namespace ProtoBench.Generation.Definitions;

public sealed record GeneratorField
{
    public string Name { get; init; } = "";
    public int Length
    {
        get; init;
    }
    public string Alignment { get; init; } = "left";
    public string Padding { get; init; } = " ";
    public bool Required { get; init; } = true;
}
public sealed record GeneratorDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Encoding { get; init; } = "ascii";
    public string LineEnding { get; init; } = "CRLF";
    public List<GeneratorField> Fields { get; init; } = [];
    public static GeneratorDefinition FromJson(string json)
    {
        var definition = JsonSerializer.Deserialize<GeneratorDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new ArgumentException("Empty generator definition.");
        definition.Validate();
        return definition;
    }
    public void Validate()
    {
        if (SchemaVersion != 1)
            throw new ArgumentException("Unsupported generator schema version.");
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Generator ID and name are required.");
        var encoding = StrictEncoding.Get(Encoding);
        if (LineEnding is not ("CRLF" or "LF" or "none"))
            throw new ArgumentException("Line ending must be CRLF, LF or none.");
        if (Fields is null || Fields.Count is 0 or > 256 || Fields.Any(f => f is null || string.IsNullOrWhiteSpace(f.Name)) || Fields.Select(f => f.Name).Distinct().Count() != Fields.Count)
            throw new ArgumentException("Supply 1–256 uniquely named fields.");
        foreach (var f in Fields)
        {
            if (f.Length <= 0 || f.Length > 65536 || f.Alignment is not ("left" or "right"))
                throw new ArgumentException($"Invalid length or alignment for '{f.Name}'.");
            if (f.Padding is null || f.Padding.Length != 1 || f.Padding[0] is '\r' or '\n' || encoding.GetByteCount(f.Padding) != 1)
                throw new ArgumentException($"Padding for '{f.Name}' must be one encodable, single-byte character.");
        }
        if (Fields.Sum(f => (long)f.Length) > 1048576)
            throw new ArgumentException("Record exceeds the 1 MiB limit.");
    }
}
