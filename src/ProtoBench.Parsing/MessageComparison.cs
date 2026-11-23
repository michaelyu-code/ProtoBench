using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;

namespace ProtoBench.Parsing;

public enum FieldComparisonStatus { Match, Changed, MissingFromMessage1, MissingFromMessage2 }

public sealed record FieldComparison(string Path, string Name, int Depth, string? Message1, string? Message2,
    FieldComparisonStatus Status)
{
    public string StatusText => Status switch
    {
        FieldComparisonStatus.Match => "Match",
        FieldComparisonStatus.Changed => "Changed",
        FieldComparisonStatus.MissingFromMessage1 => "Missing in message 1",
        _ => "Missing in message 2"
    };
    public string Value1 => Message1 is null ? "(missing)" : Message1.Length == 0 ? "(empty)" : Message1;
    public string Value2 => Message2 is null ? "(missing)" : Message2.Length == 0 ? "(empty)" : Message2;
}

public static class MessageComparison
{
    public static IReadOnlyList<FieldComparison> Compare(ParsedRecord first, ParsedRecord second,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<FieldComparison>();
        Visit(first.Fields, second.Fields, "", 0);
        return rows;

        void Visit(IReadOnlyList<ParsedField> left, IReadOnlyList<ParsedField> right, string parent, int depth)
        {
            var before = Index(left);
            var after = Index(right);
            foreach (var key in before.Keys.Concat(after.Keys).Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                before.TryGetValue(key, out var a);
                after.TryGetValue(key, out var b);
                var field = a ?? b!;
                var label = key.Id + (key.Occurrence > 1 ? $" [{key.Occurrence}]" : "");
                var path = parent.Length == 0 ? label : parent + " / " + label;
                var position = rows.Count;
                var status = a is null ? FieldComparisonStatus.MissingFromMessage1
                    : b is null ? FieldComparisonStatus.MissingFromMessage2
                    : a.Value == b.Value ? FieldComparisonStatus.Match : FieldComparisonStatus.Changed;
                rows.Add(new(path, field.Name, depth, a?.Value, b?.Value, status));
                Visit(a?.Children ?? [], b?.Children ?? [], path, depth + 1);
                if (status == FieldComparisonStatus.Match && rows.Skip(position + 1).Any(r => r.Status != FieldComparisonStatus.Match))
                    rows[position] = rows[position] with { Status = FieldComparisonStatus.Changed };
            }
        }
    }

    // Match within each parent by spec field ID (or name), then occurrence. Repeated tags stay distinct.
    private static Dictionary<(string Id, int Occurrence), ParsedField> Index(IReadOnlyList<ParsedField> fields)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new Dictionary<(string, int), ParsedField>();
        foreach (var field in fields)
        {
            var id = field.DataElement ?? field.Name;
            counts.TryGetValue(id, out var count);
            counts[id] = ++count;
            result.Add((id, count), field);
        }
        return result;
    }
}
