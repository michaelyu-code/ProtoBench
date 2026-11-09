namespace NonStopToolkit.Infrastructure.Logs;

public sealed record LogTableColumn(string Key, string Header);
public sealed record LogTableRecord(long Id, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>Field issue severity: 1 warning/review, 2 violation. Present fields without findings are green.</summary>
    public IReadOnlyDictionary<string, int> Checks { get; init; } = new Dictionary<string, int>();
}
public sealed record LogTablePage(IReadOnlyList<LogTableColumn> Columns, IReadOnlyList<LogTableRecord> Rows, bool ValuesShortened);

public static class LogTable
{
    public const int MaximumFieldColumns = 512;
    public const int MaximumCellCharacters = 4096;
    public const int MaximumPageCharacters = 4 * 1024 * 1024;
    public static IReadOnlyList<LogTableColumn> MetadataColumns { get; } = [
        new("meta:record", "Record / source line"), new("meta:date", "Date / time"),
        new("meta:direction", "Direction"), new("meta:role", "Request / reply"), new("meta:checks", "Checks")];
}
