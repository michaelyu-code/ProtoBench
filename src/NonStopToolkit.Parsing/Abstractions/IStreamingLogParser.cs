using NonStopToolkit.Core.Diagnostics;
using NonStopToolkit.Core.Documents;

namespace NonStopToolkit.Parsing.Abstractions;

/// <summary>Optional, additive plugin capability for logs of any total size. Do not retain emitted records.</summary>
public interface IStreamingLogParser : ILogParser
{
    Task ParseToAsync(Stream input, ILogParseSink sink, CancellationToken cancellationToken = default);
}

/// <summary>Called sequentially on the parsing worker. The host persists each record before returning.</summary>
public interface ILogParseSink
{
    void AddRecord(ParsedRecord record);
    void AddDiagnostic(ParseDiagnostic diagnostic);
    void ReportProgress(long bytesRead);
}
