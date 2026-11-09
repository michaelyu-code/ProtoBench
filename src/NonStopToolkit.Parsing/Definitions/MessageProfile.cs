using System.Text.Json;
using System.Text.Json.Serialization;

namespace NonStopToolkit.Parsing.Definitions;

public sealed record MessageFieldDefinition(string Id, string Name, string[] Labels,
    string Type = "text", int? Length = null, int? MaxLength = null, string? Decoder = null, string? Description = null);
public sealed record MessageFieldRule(string Field, string Usage, string Comment = "",
    string? WhenField = null, string[]? WhenValues = null, bool ForbidOtherwise = false, string? Name = null,
    bool WhenPrefix = false, int? Length = null, bool RequireWhenMatched = true);
public sealed record MessageTypeDefinition(string Name, string Role, string[] Replies, MessageFieldRule[] Fields);
public sealed record MessageProfile(int SchemaVersion, string Id, string Name, string SpecVersion,
    MessageFieldDefinition[] Fields, Dictionary<string, MessageTypeDefinition> Messages,
    Dictionary<string, string> TransactionTypes, Dictionary<string, string> Accounts,
    string[] SourceAccountRequiredFor, string[] DestinationAccountRequiredFor, int CorrelationWindowSeconds = 300)
{
    public string[] CorrelationKeys { get; init; } = ["11", "41"];
    public bool CorrelationSameConnection { get; init; } = true;

    public static MessageProfile FromJson(string json)
    {
        var profile = JsonSerializer.Deserialize<MessageProfile>(json, new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new ArgumentException("Empty message profile.");
        profile.Validate();
        return profile;
    }

    public void Validate()
    {
        if (!new NonStopToolkit.Core.Documents.MessageCorrelation(CorrelationWindowSeconds, []) { Keys = CorrelationKeys }.IsValid())
            throw new ArgumentException("Correlation needs a 1–86400 second window and 1–32 distinct, nonempty field keys of at most 128 characters.");
        if (SchemaVersion != 1 || string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(SpecVersion) || CorrelationWindowSeconds is < 1 or > 86400)
            throw new ArgumentException("Profile needs schema version 1, ID, name and spec version.");
        if (Fields is null || Messages is null || TransactionTypes is null || Accounts is null ||
            SourceAccountRequiredFor is null || DestinationAccountRequiredFor is null || Fields.Length == 0 || Fields.Length > 256 ||
            Fields.Any(f => f is null) || Fields.Select(f => f.Id).Distinct().Count() != Fields.Length)
            throw new ArgumentException("Invalid profile collections or duplicate field IDs.");
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Fields)
        {
            if (string.IsNullOrWhiteSpace(f.Id) || string.IsNullOrWhiteSpace(f.Name) || f.Labels is null ||
                f.Type is not ("text" or "digits" or "hex") || f.Length is <= 0 || f.MaxLength is <= 0 ||
                (f.Decoder is not null && (string.IsNullOrWhiteSpace(f.Decoder) || f.Decoder.Length > 128)))
                throw new ArgumentException($"Invalid field definition: {f.Id}.");
            foreach (var label in f.Labels)
                if (string.IsNullOrWhiteSpace(label) || !labels.Add(label) || label.Contains('=') || label.Length > 80)
                    throw new ArgumentException($"Invalid or conflicting label: {label}.");
        }
        foreach (var (mti, message) in Messages)
        {
            if (mti.Length != 4 || !mti.All(char.IsAsciiDigit) || message is null || message.Fields is null || message.Replies is null ||
                message.Role is not ("request" or "reply") || message.Fields.Any(r => r is null) ||
                message.Fields.Select(r => r.Field).Distinct().Count() != message.Fields.Length)
                throw new ArgumentException($"Invalid message definition: {mti}.");
            foreach (var r in message.Fields)
                if (!Fields.Any(f => f.Id == r.Field) || r.Usage is not ("M" or "C" or "O" or "F") || r.Length is <= 0 ||
                    (r.WhenField is not null && (!Fields.Any(f => f.Id == r.WhenField) || r.WhenValues is not { Length: > 0 })))
                    throw new ArgumentException($"Invalid field rule in {mti}: {r.Field}.");
            if (message.Replies.Any(reply => !Messages.ContainsKey(reply))) throw new ArgumentException("Unknown reply MTI.");
        }
    }
}
