using ProtoBench.Core.Fields;
using ProtoBench.Core.Documents;

namespace ProtoBench.Infrastructure.Logs;

public sealed partial class LogStore
{
    private readonly Dictionary<string, LogTableColumn> tableColumns = new(StringComparer.Ordinal);
    private readonly HashSet<string> missingTableColumns = new(StringComparer.Ordinal);
    public bool TableColumnsLimited { get; private set; }
    public IReadOnlyList<LogTableColumn> TableColumns => LogTable.MetadataColumns.Concat(tableColumns.Values
        .OrderBy(c => c.Key == "field:MTI" ? -2 : c.Key == "field:BITMAP" ? -1 :
            int.TryParse(c.Key[6..], out var n) ? n : int.MaxValue).ThenBy(c => c.Key, StringComparer.Ordinal)).ToArray();

    private void RememberTableColumn(ParsedField field)
    {
        var id = string.IsNullOrEmpty(field.DataElement) ? field.Name : field.DataElement;
        if (id.Length > 256) { TableColumnsLimited = true; return; }
        var key = "field:" + id.ToUpperInvariant();
        var known = tableColumns.ContainsKey(key);
        if (known && !missingTableColumns.Remove(key)) return;
        if (!known && tableColumns.Count == LogTable.MaximumFieldColumns) { TableColumnsLimited = true; return; }
        var header = field.DataElement is "mti" ? "MTI" : field.DataElement is "bitmap" ? "Bitmap"
            : int.TryParse(field.DataElement, out _) ? $"DE {field.DataElement} · {field.Name}" : field.Name;
        if (header.Length > 256) header = header[..256] + "…";
        tableColumns[key] = new(key, header);
    }

    private void IndexTableChecks(ParsedRecord record, long id)
    {
        var issues = new Dictionary<string, int>(StringComparer.Ordinal);
        var findings = record.Findings.ToLookup(f => f.Field, StringComparer.Ordinal);
        foreach (var field in record.Fields)
        {
            var fieldId = string.IsNullOrEmpty(field.DataElement) ? field.Name : field.DataElement;
            if (fieldId.Length > 256) continue;
            var key = "field:" + fieldId.ToUpperInvariant();
            if (!tableColumns.ContainsKey(key)) continue;
            var matches = findings[field.DataElement ?? field.Name];
            if (field.DataElement is not null && field.DataElement != field.Name) matches = matches.Concat(findings[field.Name]);
            var severity = matches.Select(f => f.Status is "Missing" or "Invalid" or "Forbidden" ? 2 : 1).DefaultIfEmpty(0).Max();
            if (severity > 0) issues[key] = Math.Max(issues.GetValueOrDefault(key), severity);
        }
        foreach (var missing in record.Findings.Where(f => f.Status == "Missing"))
        {
            if (missing.Field.Length > 256) { TableColumnsLimited = true; continue; }
            var key = "field:" + missing.Field.ToUpperInvariant();
            if (!tableColumns.ContainsKey(key) && tableColumns.Count < LogTable.MaximumFieldColumns)
            {
                tableColumns[key] = new(key, int.TryParse(missing.Field, out _) ? $"DE {missing.Field} · Missing field" : missing.Field);
                missingTableColumns.Add(key);
            }
            else if (!tableColumns.ContainsKey(key)) TableColumnsLimited = true;
            if (tableColumns.ContainsKey(key)) issues[key] = 2;
        }
        insertFieldCheck.Transaction = transaction;
        foreach (var (key, severity) in issues)
        {
            insertFieldCheck.Parameters["$id"].Value = id;
            insertFieldCheck.Parameters["$key"].Value = key;
            insertFieldCheck.Parameters["$severity"].Value = severity;
            insertFieldCheck.ExecuteNonQuery();
        }
    }

    /// <summary>Read raw field projections for the existing page, without unpacking decoded records.</summary>
    public Task<LogTablePage> ReadTableAsync(IReadOnlyList<LogRecordSummary> page, IReadOnlyList<string> keys, CancellationToken token = default)
    {
        if (page.Count > LogQuery.MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(page));
        var rows = page.ToArray();
        var selected = keys.Distinct(StringComparer.Ordinal).ToArray();
        return RunAsync(() =>
        {
            var catalog = TableColumns.ToDictionary(c => c.Key, StringComparer.Ordinal);
            if (selected.Any(k => !catalog.ContainsKey(k))) throw new ArgumentException("Unknown table column.", nameof(keys));
            var columns = selected.Select(k => catalog[k]).ToArray();
            var values = rows.ToDictionary(r => r.Id, _ => new Dictionary<string, string>(StringComparer.Ordinal));
            var checks = rows.ToDictionary(r => r.Id, _ => new Dictionary<string, int>(StringComparer.Ordinal));
            var shortened = false;
            var remaining = LogTable.MaximumPageCharacters;
            string Clip(string value)
            {
                var count = Math.Min(value.Length, Math.Min(LogTable.MaximumCellCharacters, remaining));
                remaining -= count;
                if (count == value.Length) return value;
                shortened = true;
                return value[..count] + "\n… [table preview shortened; open message for full value]";
            }
            foreach (var row in rows)
            foreach (var key in selected.Where(k => k.StartsWith("meta:", StringComparison.Ordinal)))
                values[row.Id][key] = Clip(key switch
                {
                    "meta:record" => row.Message?.RecordNumber is { Length: > 0 } number ? number : $"Source line {row.LineNumber}",
                    "meta:date" => row.Message?.LoggedDateTime ?? "unknown",
                    "meta:direction" => row.Message?.Direction ?? "unknown",
                    "meta:role" => row.Message?.Role ?? "unknown",
                    "meta:checks" => $"{row.Errors} issues · {row.Findings - row.Errors} warnings",
                    _ => ""
                });
            var fields = selected.Where(k => k.StartsWith("field:", StringComparison.Ordinal)).ToArray();
            if (rows.Length > 0 && fields.Length > 0)
            {
                using var command = connection.CreateCommand();
                command.CommandTimeout = 0;
                var rowParameters = rows.Select((r, i) => { var p = "$r" + i; command.Parameters.AddWithValue(p, r.Id); return p; });
                var fieldParameters = fields.Select((f, i) => { var p = "$f" + i; command.Parameters.AddWithValue(p, f); return p; });
                const string fieldKey = "('field:' || CASE WHEN element<>'' THEN element ELSE name END)";
                command.CommandText = $"SELECT record_id,{fieldKey},substr(value,1,{LogTable.MaximumCellCharacters + 1}),length(value) FROM fields "
                    + $"WHERE record_id IN ({string.Join(',', rowParameters)}) AND {fieldKey} IN ({string.Join(',', fieldParameters)}) ORDER BY record_id,rowid";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    CheckCancellation();
                    var row = values[reader.GetInt64(0)];
                    var key = reader.GetString(1);
                    var text = reader.GetString(2);
                    if (row.TryGetValue(key, out var previous)) text = previous + "\n" + text;
                    row[key] = Clip(text);
                    if (reader.GetInt64(3) > LogTable.MaximumCellCharacters) shortened = true;
                }
            }
            if (rows.Length > 0 && fields.Length > 0)
            {
                using var command = connection.CreateCommand();
                command.CommandTimeout = 0;
                var rowParameters = rows.Select((r, i) => { var p = "$r" + i; command.Parameters.AddWithValue(p, r.Id); return p; });
                var fieldParameters = fields.Select((f, i) => { var p = "$f" + i; command.Parameters.AddWithValue(p, f); return p; });
                command.CommandText = $"SELECT record_id,column_key,severity FROM field_checks WHERE record_id IN ({string.Join(',', rowParameters)}) AND column_key IN ({string.Join(',', fieldParameters)})";
                using var reader = command.ExecuteReader();
                while (reader.Read()) { CheckCancellation(); checks[reader.GetInt64(0)][reader.GetString(1)] = reader.GetInt32(2); }
            }
            return new LogTablePage(columns, rows.Select(r => new LogTableRecord(r.Id, values[r.Id]) { Checks = checks[r.Id] }).ToArray(), shortened);
        }, token);
    }
}
