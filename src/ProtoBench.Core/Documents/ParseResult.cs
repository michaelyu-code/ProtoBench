using ProtoBench.Core.Fields;
using ProtoBench.Core.Diagnostics;
namespace ProtoBench.Core.Documents;

public sealed record MessageMetadata(string ProfileId, string SpecVersion, string RecordNumber, string LoggedDateTime,
    string Direction, string ConnectionId, string Sequence, string MessageType, string Role, string Process = "");
public sealed record FieldFinding(string Field, string Status, string Explanation, string RuleId);
/// <summary>On-demand explanation of a check, kept separate from issue counts.</summary>
public sealed record FieldCheck(string Field, string Check, string Outcome, string Detail, string RuleId);
/// <summary>Explicit profile rules for related-message lookup, retained through streaming and plugin wrappers.</summary>
public sealed record MessageCorrelation(int WindowSeconds, IReadOnlyList<string> MessageTypes)
{
    public IReadOnlyList<string> Keys { get; init; } = Array.AsReadOnly(new[] { "11", "41" });
    public bool SameConnection { get; init; } = true;
    public bool IsValid() => WindowSeconds is >= 1 and <= 86400 && MessageTypes is not null
        && Keys is { Count: > 0 and <= 32 } && Keys.All(k => !string.IsNullOrWhiteSpace(k) && k.Length <= 128)
        && Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() == Keys.Count;
}
public sealed record ParsedRecord(long LineNumber, string RawText, IReadOnlyList<ParsedField> Fields)
{
    public MessageMetadata? Message { get; init; }
    public MessageCorrelation? Correlation { get; init; }
    public IReadOnlyList<FieldFinding> Findings { get; init; } = [];
}
public sealed record ParseResult(IReadOnlyList<ParsedRecord> Records, IReadOnlyList<ParseDiagnostic> Diagnostics, bool IsTruncated = false);
public sealed record GenerationResult(long BytesWritten, IReadOnlyList<ParseDiagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);
}
