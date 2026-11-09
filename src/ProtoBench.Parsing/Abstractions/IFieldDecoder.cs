using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;

namespace ProtoBench.Parsing.Abstractions;

/// <summary>Reusable decoding for labelled message fields. IDs are case-sensitive and should be namespaced.</summary>
public interface IFieldDecoder
{
    string Id { get; }
    FieldDecodeResult Decode(FieldDecodeContext context, CancellationToken cancellationToken);
}

public sealed record FieldDecodeContext(string ProfileId, string MessageType, ParsedField Field,
    IReadOnlyDictionary<string, string> Values);
public sealed record FieldDecodeResult(IReadOnlyList<ParsedField> Children, IReadOnlyList<FieldFinding>? Findings = null)
{
    /// <summary>Optional preformatted plain text. Preserve spaces and line breaks to align tables; no markup is interpreted.</summary>
    public string? Specification { get; init; }
}
