using System.Text.Json;

namespace ProtoBench.PluginSdk;

/// <summary>Plain-text provenance supplied by the plugin; never inferred by the host.</summary>
public sealed record ToolSpecReference(string Specification, string Location, string Passage = "");

/// <summary>One ranked search hit. Order hits in the desired relevance order; the host does not rerank them.</summary>
public sealed record ToolSearchHit(string Title, string Excerpt, string Passage)
{
    public ToolSpecReference? Reference { get; init; }
    public double? Score { get; init; }
}

public sealed record ToolSearchResult(string Query, IReadOnlyList<ToolSearchHit> Hits)
{
    /// <summary>Only set when the engine knows the total number of matches, before the result limit.</summary>
    public int? TotalMatches { get; init; }
    public IReadOnlyList<string> HighlightTerms { get; init; } = [];
    public string EmptyMessage { get; init; } = "No matching passages. Try a shorter phrase or another keyword.";
}

public enum ToolDecodeStatus { Decoded, PartiallyDecoded, Unmapped, InvalidInput, MissingContext }

/// <summary>Preserve the raw value for unmapped components. Position is a human-readable bit/byte/digit range.</summary>
public sealed record ToolDecodeComponent(string Name, string RawValue, string Meaning)
{
    public string Position { get; init; } = "";
    public IReadOnlyList<ToolDecodeComponent> Children { get; init; } = [];
    public ToolSpecReference? Reference { get; init; }
}

public sealed record ToolContextValue(string Name, string Value);

public sealed record ToolDecodeResult(string Field, string RawValue, string Summary,
    IReadOnlyList<ToolDecodeComponent> Components)
{
    public ToolDecodeStatus Status { get; init; } = ToolDecodeStatus.Decoded;
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<ToolContextValue> ContextUsed { get; init; } = [];
    public IReadOnlyList<ToolSpecReference> References { get; init; } = [];
}

/// <summary>FieldValues inputs are JSON objects of field IDs to string values. Empty input means no fields.</summary>
public static class ToolInputValues
{
    public static IReadOnlyDictionary<string, string> ParseFieldValues(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, string>();
        using var document = JsonDocument.Parse(text);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Supporting fields must be a JSON object.");
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Each supporting field needs a name and a string value.");
            if (!fields.TryAdd(property.Name.Trim(), property.Value.GetString()!))
                throw new ArgumentException($"Duplicate supporting field: {property.Name}.");
        }
        return fields;
    }
}
