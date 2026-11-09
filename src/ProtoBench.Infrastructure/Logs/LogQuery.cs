using ProtoBench.Core.Documents;

namespace ProtoBench.Infrastructure.Logs;

public sealed record LogQuery(string Search = "", string Field = "", string Direction = "", string Role = "",
    string Sort = "record", bool Descending = false, long PageIndex = 0, int PageSize = LogQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 2000;
    public const int MaximumPageSize = 2000;
}

// Pages contain small summaries only. The full decoded payload is loaded for the selected message.
public sealed record LogRecordSummary(long Id, long LineNumber, MessageMetadata? Message, string Stan,
    string Terminal, string Acceptor, int Errors, int Findings);
public sealed record LogPage(IReadOnlyList<LogRecordSummary> Rows, long Matches, long Total, long PageIndex, int PageSize)
{
    public long PageCount => Math.Max(1, (Matches + PageSize - 1) / PageSize);
}
public sealed record LogImportProgress(long BytesRead, long Records, string Stage = "Parsing");
