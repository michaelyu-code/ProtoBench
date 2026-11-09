using System.Text;
using System.Text.RegularExpressions;
using ProtoBench.Core.Diagnostics;
using ProtoBench.Parsing.Abstractions;

namespace ProtoBench.Parsing.Engines;

public sealed partial class RecordMessageParser
{
    public async Task ParseToAsync(Stream input, ILogParseSink sink, CancellationToken cancellationToken = default)
    {
        var raw = new StringBuilder();
        long messageOffset = 0, messageLine = 1;
        bool active = false, invalid = false, found = false, preamble = false, hasMti = false;
        await foreach (var line in LogLineReader.ReadAsync(input, cancellationToken))
        {
            sink.ReportProgress(input.CanSeek ? input.Position : line.Offset + line.Bytes.Length);
            string text;
            try { text = line.Oversized ? "" : Utf8.GetString(line.Bytes); }
            catch (DecoderFallbackException)
            {
                sink.AddDiagnostic(new("parse.encoding", "Line contains invalid UTF-8; its message was skipped.", Line: line.Number));
                invalid = true;
                continue;
            }
            var bom = line.Offset == 0 && text.StartsWith('\uFEFF');
            if (bom) text = text[1..];
            bool header = false, mtiStart = false;
            try { header = Header.IsMatch(text); mtiStart = !header && messageStart.IsMatch(text); }
            catch (RegexMatchTimeoutException) { sink.AddDiagnostic(new("parse.header-timeout", "Header exceeded the 100 ms limit.", Line: line.Number)); invalid = true; }
            if (header || (mtiStart && (!active || hasMti || invalid)))
            {
                Emit();
                active = found = true;
                invalid = false;
                hasMti = false;
                messageOffset = line.Offset + (bom ? 3 : 0);
                messageLine = line.Number;
            }
            if (mtiStart) hasMti = true;
            if (!active)
            {
                if (!preamble && !string.IsNullOrWhiteSpace(text))
                {
                    preamble = true;
                    sink.AddDiagnostic(new("parse.preamble", "Text before the first message was skipped.", DiagnosticSeverity.Warning));
                }
                continue;
            }
            if (line.Oversized || raw.Length + text.Length > LogLineReader.MaxRecordBytes)
            {
                if (!invalid) sink.AddDiagnostic(new("parse.message-limit", "Message exceeds the 4 MiB character/line bound and was skipped; parsing continued.", Line: messageLine));
                invalid = true;
                raw.Clear();
            }
            if (!invalid) raw.Append(text);
        }
        Emit();
        if (!found) sink.AddDiagnostic(new("parse.header", "No RECORD message headers found."));
        cancellationToken.ThrowIfCancellationRequested();

        void Emit()
        {
            if (active && !invalid)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = raw.ToString();
                try
                {
                    var header = Header.Match(text);
                    sink.AddRecord(ParseRecord(text, header, messageLine, messageOffset, cancellationToken));
                    if (!header.Success) sink.AddDiagnostic(new("parse.header-missing", "Recovered message from its MTI field; RECORD header details are unknown.", DiagnosticSeverity.Warning, Line: messageLine));
                }
                catch (RegexMatchTimeoutException) { sink.AddDiagnostic(new("parse.record-timeout", "Message exceeded the 100 ms regex limit and was skipped.", Line: messageLine)); }
            }
            raw.Clear();
        }
    }
}
