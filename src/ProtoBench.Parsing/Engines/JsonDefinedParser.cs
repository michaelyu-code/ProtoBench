using System.Text;
using System.Text.RegularExpressions;
using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;
using ProtoBench.Core.Diagnostics;
using ProtoBench.Core.Encoding;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Definitions;
namespace ProtoBench.Parsing.Engines;

public sealed partial class JsonDefinedParser : IStreamingLogParser
{
    private readonly ParserDefinition definition;
    private readonly System.Text.Encoding encoding;
    private readonly Regex? regex;
    public const int MaxRecords = 10000;
    public const int MaxLineBytes = 65536;
    public const int MaxPreviewBytes = 16 * 1024 * 1024;
    public string FormatId => definition.Id;
    public string Name => definition.Name;
    public int DetectionPriority => definition.DetectionPriority;
    public JsonDefinedParser(ParserDefinition definition)
    {
        definition.Validate();
        this.definition = definition with
        {
            Fields = definition.Fields.ToList()
        };
        encoding = StrictEncoding.Get(definition.Encoding);
        if (definition.Strategy == "regex")
            regex = new Regex(definition.Pattern!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
    public bool CanParse(ReadOnlySpan<byte> sample)
    {
        // Prefix comparison avoids decoding a truncated multi-byte sample.
        if (definition.Encoding.Equals("utf-8", StringComparison.OrdinalIgnoreCase) && sample.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) sample = sample[3..];
        return !string.IsNullOrEmpty(definition.DetectionPrefix) && sample.StartsWith(encoding.GetBytes(definition.DetectionPrefix));
    }
    public async Task<ParseResult> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var records = new List<ParsedRecord>();
        var diagnostics = new List<ParseDiagnostic>();
        var buffer = new byte[8192];
        var line = new List<byte>();
        long offset = 0, lineStart = 0, lineNumber = 0;
        bool truncated = false;
        void AddLine()
        {
            lineNumber++;
            var bytes = line.ToArray();
            if (bytes.Length > 0 && bytes[^1] == 13)
                bytes = bytes[..^1];
            try
            {
                var text = encoding.GetString(bytes);
                // BOM is only accepted at the start of a UTF-8 stream and counts towards byte offsets.
                int bomBytes = lineStart == 0 && text.StartsWith('﻿') ? 3 : 0;
                if (bomBytes != 0)
                    text = text[1..];
                var fields = Extract(text, lineStart + bomBytes);
                records.Add(new ParsedRecord(lineNumber, text, fields));
            }
            catch (Exception ex) when (ex is FormatException or DecoderFallbackException or EncoderFallbackException or RegexMatchTimeoutException)
            {
                diagnostics.Add(new ParseDiagnostic("parse.record", ex is RegexMatchTimeoutException ? "Regex exceeded the 100 ms record limit." : ex is DecoderFallbackException ? "Record contains invalid encoded bytes." : ex.Message, Line: lineNumber));
            }
            line.Clear();
        }
        while (true)
        {
            var count = await input.ReadAsync(buffer, cancellationToken);
            if (count == 0)
                break;
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var b = buffer[i];
                offset++;
                if (offset > MaxPreviewBytes)
                {
                    diagnostics.Add(new ParseDiagnostic("parse.byte-limit", "Preview stopped after 16 MiB of source data.", DiagnosticSeverity.Warning));
                    return new(records, diagnostics, true);
                }
                if (b == 10)
                {
                    AddLine();
                    lineStart = offset;
                    if (lineNumber >= MaxRecords)
                    {
                        truncated = true;
                        break;
                    }
                }
                else
                {
                    line.Add(b);
                    if (line.Count > MaxLineBytes)
                    {
                        diagnostics.Add(new ParseDiagnostic("parse.line-limit", "Record exceeds 65,536 bytes; parsing stopped.", Line: lineNumber + 1));
                        return new(records, diagnostics, true);
                    }
                }
            }
            if (truncated)
                break;
        }
        if (!truncated && line.Count > 0)
            AddLine();
        if (truncated)
            diagnostics.Add(new ParseDiagnostic("parse.preview-limit", "Preview stopped at 10,000 records.", DiagnosticSeverity.Warning));
        return new(records, diagnostics, truncated);
    }
    private IReadOnlyList<ParsedField> Extract(string text, long byteStart)
    {
        var ranges = new List<(int Start, int Length)>();
        if (definition.Strategy == "delimited")
        {
            int start = 0, next;
            while ((next = text.IndexOf(definition.Delimiter, start, StringComparison.Ordinal)) >= 0)
            {
                ranges.Add((start, next - start));
                start = next + definition.Delimiter.Length;
            }
            ranges.Add((start, text.Length - start));
            if (ranges.Count != definition.Fields.Count)
                throw new FormatException($"Expected {definition.Fields.Count} delimited fields; found {ranges.Count}.");
        }
        else if (definition.Strategy == "fixed-width")
            ranges.AddRange(definition.Fields.Select(f => (f.Start, f.Length)));
        else
        {
            var match = regex!.Match(text);
            if (!match.Success)
                throw new FormatException("Record does not match the expression.");
            foreach (var f in definition.Fields)
            {
                var group = match.Groups[f.Name];
                if (!group.Success)
                    throw new FormatException($"Missing regex group '{f.Name}'.");
                ranges.Add((group.Index, group.Length));
            }
        }
        var fields = new List<ParsedField>();
        for (var i = 0; i < definition.Fields.Count; i++)
        {
            var f = definition.Fields[i];
            var (start, length) = ranges[i];
            if ((long)start + length > text.Length)
                throw new FormatException($"Record is too short for field '{f.Name}'.");
            var raw = text.Substring(start, length);
            var value = f.Transform switch
            {
                "trim" => raw.Trim(),
                "upper" => raw.ToUpperInvariant(),
                "lower" => raw.ToLowerInvariant(),
                _ => raw
            };
            fields.Add(new ParsedField(f.Name, value, new SourceRange(byteStart + encoding.GetByteCount(text.AsSpan(0, start)), encoding.GetByteCount(raw), start, length)));
        }
        return fields;
    }
}
