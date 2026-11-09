namespace ProtoBench.Core.Fields;
// Offsets are explicitly relative to the containing record's decoded text or original stream.
public sealed record SourceRange(long ByteOffset, int ByteLength, int CharacterOffset, int CharacterLength);
public sealed record ParsedField(string Name, string Value, SourceRange Source, IReadOnlyList<ParsedField>? Children = null)
{
    public string? OriginalLabel { get; init; }
    public string? DataElement { get; init; }
    public string? Description { get; init; }
    /// <summary>Plugin-supplied plain text, displayed verbatim in a monospaced, non-wrapping Specification tab.</summary>
    public string? Specification { get; init; }
}
