using System.Globalization;
using NonStopToolkit.Core.Documents;

namespace NonStopToolkit.Parsing.Engines;

public static class MessageAnalysis
{
    public static DateTime? LoggedTime(ParsedRecord record) => DateTime.TryParseExact("2000 " + record.Message?.LoggedDateTime,
        "yyyy dMMM HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    public static string Value(ParsedRecord record, string id) => record.Fields.FirstOrDefault(f =>
        string.Equals(f.DataElement, id, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Name, id, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(f.OriginalLabel, id, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

    public static IReadOnlyList<ParsedRecord> Filter(IEnumerable<ParsedRecord> records, string search = "", string field = "",
        string direction = "", string role = "", string sort = "record", bool descending = false)
    {
        var filtered = records.Where(r => (direction.Length == 0 || r.Message?.Direction == direction) &&
            (role.Length == 0 || r.Message?.Role == role) &&
            (field.Length == 0 ? search.Length == 0 || r.RawText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                r.Fields.Any(f => f.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || f.Value.Contains(search, StringComparison.OrdinalIgnoreCase)) :
                r.Fields.Any(f => (string.Equals(f.DataElement, field, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase) || string.Equals(f.OriginalLabel, field, StringComparison.OrdinalIgnoreCase)) &&
                    f.Value.Contains(search, StringComparison.OrdinalIgnoreCase))));
        string Key(ParsedRecord r) => sort switch
        {
            "record" => long.TryParse(r.Message?.RecordNumber, out var number) ? number.ToString("D20") : r.LineNumber.ToString("D20"),
            "datetime" => LoggedTime(r)?.ToString("MMddHHmmssfffffff", CultureInfo.InvariantCulture) ?? "",
            "sequence" => r.Message?.Sequence.PadLeft(20, '0') ?? "",
            "mti" => r.Message?.MessageType ?? "",
            _ => Value(r, sort)
        };
        return (descending ? filtered.OrderByDescending(Key, StringComparer.OrdinalIgnoreCase) : filtered.OrderBy(Key, StringComparer.OrdinalIgnoreCase)).ToArray();
    }

    // Return all candidates. Reused STANs and repeated requests must not silently form a unique pair.
    public static IReadOnlyList<ParsedRecord> Related(ParsedRecord selected, IEnumerable<ParsedRecord> records, MessageProfileLookup lookup)
        => Related(selected, records, new MessageCorrelation(lookup.WindowSeconds, [])
            { Keys = lookup.Keys, SameConnection = lookup.SameConnection }, (a, b) => lookup.IsReply(a, b) || lookup.IsReply(b, a));

    public static IReadOnlyList<ParsedRecord> Related(ParsedRecord selected, IEnumerable<ParsedRecord> records, MessageCorrelation correlation)
        => Related(selected, records, correlation, (_, other) => correlation.MessageTypes.Contains(other, StringComparer.Ordinal));

    private static IReadOnlyList<ParsedRecord> Related(ParsedRecord selected, IEnumerable<ParsedRecord> records,
        MessageCorrelation correlation, Func<string, string, bool> compatible)
    {
        var m = selected.Message;
        if (m is null || !correlation.IsValid() || LoggedTime(selected) is not { } selectedTime) return [];
        var keys = correlation.Keys.Select(k => (Key: k, Value: Value(selected, k))).ToArray();
        if (keys.Any(k => k.Value.Length == 0)) return [];
        return records.Where(r => !ReferenceEquals(r, selected) && r.Message is { } other &&
            other.ProfileId == m.ProfileId && (!correlation.SameConnection || other.ConnectionId == m.ConnectionId) && other.Direction != m.Direction &&
            keys.All(k => Value(r, k.Key) == k.Value) &&
            LoggedTime(r) is { } otherTime &&
            Math.Abs((otherTime - selectedTime).TotalSeconds) <= correlation.WindowSeconds &&
            compatible(m.MessageType, other.MessageType)).ToArray();
    }
}

public sealed class MessageProfileLookup(NonStopToolkit.Parsing.Definitions.MessageProfile profile)
{
    public int WindowSeconds => profile.CorrelationWindowSeconds;
    public IReadOnlyList<string> Keys => profile.CorrelationKeys;
    public bool SameConnection => profile.CorrelationSameConnection;
    public bool IsReply(string request, string reply) => profile.Messages.TryGetValue(request, out var definition) && definition.Replies.Contains(reply);
}
