using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ProtoBench.Core.Diagnostics;
using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Utilities.Payments;

namespace ProtoBench.Parsing.Engines;

/// <summary>Reads the RECORD / FROM / IN|OUT ON / SEQ key-value log layout.</summary>
public sealed partial class RecordMessageParser : IStreamingLogParser
{
    // WinUI TextBox may return CR-only newlines. Match boundaries without rewriting
    // the original text, so byte/character source offsets remain accurate.
    private static readonly Regex Header = new(@"(?:\A|(?<=[\r\n]))[ \t]*(?<process>\S+)[ \t]+RECORD[ \t]+(?<record>\d+)[ \t]+FROM[ \t]+(?<date>\d{1,2}[A-Za-z]{3}[ \t]+\d{2}:\d{2}:\d{2}\.\d+)[ \t]+(?<direction>IN|OUT)[ \t]+ON[ \t]+(?<connection>\S+)[ \t]+SEQ:[ \t]*(?<sequence>\d+)[^\r\n]*", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly MessageProfile profile;
    private readonly Regex labels;
    private readonly Regex messageStart;
    private readonly Dictionary<string, MessageFieldDefinition> aliases;
    private readonly IReadOnlyDictionary<string, IFieldDecoder> decoders;
    private readonly IReadOnlyDictionary<string, MessageCorrelation> correlations;
    public string FormatId => profile.Id;
    public string Name => profile.Name;
    public int DetectionPriority => 100;
    public int CorrelationWindowSeconds => profile.CorrelationWindowSeconds;
    public IReadOnlyList<ParsedRecord> Related(ParsedRecord selected, IEnumerable<ParsedRecord> records) =>
        MessageAnalysis.Related(selected, records, new MessageProfileLookup(profile));
    public MessageCorrelation? GetCorrelation(string messageType) => correlations.GetValueOrDefault(messageType);

    public RecordMessageParser(MessageProfile profile, IEnumerable<IFieldDecoder>? fieldDecoders = null)
    {
        profile.Validate();
        // Take a private snapshot: caller mutations cannot change parsing rules mid-import.
        this.profile = MessageProfile.FromJson(System.Text.Json.JsonSerializer.Serialize(profile));
        var pairs = this.profile.Messages.Keys.ToDictionary(mti => mti, _ => new HashSet<string>(StringComparer.Ordinal));
        foreach (var (request, definition) in this.profile.Messages)
        foreach (var reply in definition.Replies) { pairs[request].Add(reply); pairs[reply].Add(request); }
        correlations = pairs.ToDictionary(p => p.Key, p => new MessageCorrelation(this.profile.CorrelationWindowSeconds,
            Array.AsReadOnly(p.Value.Order(StringComparer.Ordinal).ToArray()))
        {
            Keys = Array.AsReadOnly(this.profile.CorrelationKeys), SameConnection = this.profile.CorrelationSameConnection
        });
        decoders = (fieldDecoders ?? []).ToDictionary(d => d.Id, StringComparer.Ordinal);
        aliases = this.profile.Fields.SelectMany(f => f.Labels.Select(label => (label, f)))
            .ToDictionary(p => p.label, p => p.f, StringComparer.OrdinalIgnoreCase);
        var known = string.Join("|", aliases.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        // Known aliases take priority. The fallback retains unknown labels for future spec revisions.
        labels = new Regex(@"(?<!\S)(?<label>" + known + @"|Fld[ ]*\d+|[A-Za-z][A-Za-z0-9_-]*)[ ]*=",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        var mtiLabels = this.profile.Fields.FirstOrDefault(f => f.Id == "mti")?.Labels ?? ["Msg-type"];
        messageStart = new Regex(@"(?:\A|(?<=[\r\n]))[ \t]*(?:" + string.Join("|", mtiLabels.Select(Regex.Escape)) +
            @")[ ]*=[ \t]*(?<mti>[0-9]{4})(?=[ \t\r\n]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
    public bool CanParse(ReadOnlySpan<byte> sample)
    {
        var text = Encoding.UTF8.GetString(sample).TrimStart('\uFEFF');
        return Header.IsMatch(text) || messageStart.Matches(text).Cast<Match>().Any(m => profile.Messages.ContainsKey(m.Groups["mti"].Value));
    }

    private (int Index, Match Header)[] MessageBoundaries(string text)
    {
        var starts = new List<(int, Match)>();
        bool hasMti = false;
        foreach (var candidate in Header.Matches(text).Cast<Match>().Select(m => (Match: m, IsHeader: true))
            .Concat(messageStart.Matches(text).Cast<Match>().Select(m => (Match: m, IsHeader: false))).OrderBy(x => x.Match.Index))
        {
            if (candidate.IsHeader)
            {
                starts.Add((candidate.Match.Index, candidate.Match));
                hasMti = false;
            }
            else
            {
                // The first MTI after a valid header belongs to that message. A subsequent
                // line-start MTI begins a new message whose header is missing.
                if (starts.Count == 0 || hasMti) starts.Add((candidate.Match.Index, Match.Empty));
                hasMti = true;
            }
        }
        return starts.ToArray();
    }

    public async Task<ParseResult> ParseAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ParseDiagnostic>();
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        bool truncated = false;
        while (true)
        {
            var remaining = JsonDefinedParser.MaxPreviewBytes - (int)buffer.Length;
            var count = await input.ReadAsync(bytes.AsMemory(0, Math.Min(bytes.Length, remaining + 1)), cancellationToken);
            if (count == 0) break;
            if (count > remaining)
            {
                buffer.Write(bytes, 0, remaining);
                truncated = true;
                diagnostics.Add(new("parse.byte-limit", "Preview stopped at 16 MiB; the incomplete final message is excluded.", DiagnosticSeverity.Warning));
                break;
            }
            buffer.Write(bytes, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        string text;
        try { text = Utf8.GetString(buffer.ToArray()); }
        catch (DecoderFallbackException)
        {
            // A preview cut can split UTF-8; only accept an intact prefix in that case.
            if (!truncated) return new([], [new("parse.encoding", "Input is not valid UTF-8.")]);
            var data = buffer.ToArray();
            text = "";
            bool decoded = false;
            for (var cut = 1; cut <= Math.Min(3, data.Length); cut++)
            {
                try { text = Utf8.GetString(data, 0, data.Length - cut); decoded = true; break; }
                catch (DecoderFallbackException) { }
            }
            if (!decoded) return new([], [new("parse.encoding", "Input is not valid UTF-8.")], true);
        }
        var bom = text.StartsWith('\uFEFF') ? 1 : 0;
        var matches = MessageBoundaries(text[bom..]);
        var records = new List<ParsedRecord>();
        if (matches.Length == 0) return new([], [new("parse.header", "No RECORD message headers found.")], truncated);
        if (!string.IsNullOrWhiteSpace(text[bom..(matches[0].Index + bom)]))
            diagnostics.Add(new("parse.preamble", "Text before the first message was skipped.", DiagnosticSeverity.Warning));
        int countToParse = matches.Length - (truncated ? 1 : 0);
        int previousStart = 0;
        long recordByteOffset = 0;
        long line = 1;
        for (int i = 0; i < countToParse && records.Count < JsonDefinedParser.MaxRecords; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var header = matches[i].Header;
            int start = matches[i].Index + bom;
            line += CountLineBreaks(text.AsSpan(previousStart, start - previousStart));
            recordByteOffset += Utf8.GetByteCount(text.AsSpan(previousStart, start - previousStart));
            previousStart = start;
            var end = i + 1 < matches.Length ? matches[i + 1].Index + bom : text.Length;
            var raw = text[start..end];
            if (raw.Length > 1024 * 1024)
            {
                diagnostics.Add(new("parse.message-limit", "Message exceeds 1 MiB and was skipped.", Line: line));
                continue;
            }
            records.Add(ParseRecord(raw, header, line, recordByteOffset, cancellationToken));
            if (!header.Success) diagnostics.Add(new("parse.header-missing", "Recovered message from its MTI field; RECORD header details are unknown.", DiagnosticSeverity.Warning, Line: line));
        }
        if (records.Count < countToParse)
        {
            truncated = true;
            diagnostics.Add(new("parse.preview-limit", "Some messages were excluded by preview limits; search covers loaded messages only.", DiagnosticSeverity.Warning));
        }
        return new(records, diagnostics, truncated);
    }

    private static int CountLineBreaks(ReadOnlySpan<char> text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n' || (text[i] == '\r' && (i + 1 == text.Length || text[i + 1] != '\n'))) count++;
        return count;
    }

    private ParsedRecord ParseRecord(string raw, Match header, long line, long byteOffset, CancellationToken token)
    {
        var fields = new List<ParsedField>();
        var findings = new List<FieldFinding>();
        if (!header.Success) findings.Add(new("message", "Review",
            "RECORD header is missing. Recovered from the MTI field; record number, date/time, direction, connection, sequence and process are unknown.", "message.header-missing"));
        var bodyStart = header.Length;
        var matches = labels.Matches(raw[bodyStart..]).Cast<Match>().ToArray();
        foreach (var (match, index) in matches.Select((m, i) => (m, i)))
        {
            token.ThrowIfCancellationRequested();
            var label = match.Groups["label"].Value;
            aliases.TryGetValue(label, out var definition);
            // Permit generic field-number labels even when the preferred label is not configured.
            var number = Regex.Match(label, @"^Fld\s*(\d+)$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            if (definition is null && number.Success) definition = profile.Fields.FirstOrDefault(f => f.Id == number.Groups[1].Value);
            int start = bodyStart + match.Index + match.Length;
            int end = index + 1 < matches.Length ? bodyStart + matches[index + 1].Index : raw.Length;
            while (start < end && char.IsWhiteSpace(raw[start])) start++;
            while (end > start && char.IsWhiteSpace(raw[end - 1])) end--;
            var source = new SourceRange(byteOffset + Utf8.GetByteCount(raw.AsSpan(0, start)), Utf8.GetByteCount(raw.AsSpan(start, end - start)), start, end - start);
            var value = raw[start..end].Trim();
            if (definition?.Type == "hex" || definition?.Decoder == "emvEntries")
                value = Regex.Replace(value, @"\s+", "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var id = definition?.Id ?? label;
            if (fields.Any(f => f.DataElement == id)) findings.Add(new(id, "Duplicate", "Multiple occurrences preserved.", "field.duplicate"));
            if (definition is null) findings.Add(new(id, "Unknown", "Label is not defined in this profile.", "field.unknown"));
            fields.Add(new(definition?.Name ?? label, value, source) { OriginalLabel = label, DataElement = id, Description = definition?.Description });
        }
        string Value(string id) => fields.FirstOrDefault(f => f.DataElement == id)?.Value ?? "";
        var mti = Value("mti");
        profile.Messages.TryGetValue(mti, out var message);
        if (message is null) findings.Add(new("mti", "Unknown", "No message rules for this MTI.", "message.unknown"));
        foreach (var rule in message?.Fields ?? [])
        {
            var present = fields.Any(f => f.DataElement == rule.Field);
            var ruleId = $"{profile.Id}:{mti}:{rule.Field}";
            var usage = rule.Usage;
            if (usage == "C")
            {
                if (rule.WhenField is null || Value(rule.WhenField).Length == 0)
                {
                    findings.Add(new(rule.Field, "Undetermined", rule.Comment.Length > 0 ? rule.Comment : "Condition needs detailed spec or transaction context.", ruleId));
                    continue;
                }
                var matchesCondition = rule.WhenValues!.Any(v => rule.WhenPrefix ? Value(rule.WhenField).StartsWith(v, StringComparison.Ordinal) : Value(rule.WhenField) == v);
                if (matchesCondition && !rule.RequireWhenMatched)
                    findings.Add(new(rule.Field, "Undetermined", "Applicability matches, but the excerpt does not establish required presence. " + rule.Comment, ruleId));
                usage = matchesCondition ? rule.RequireWhenMatched ? "M" : "O" : rule.ForbidOtherwise ? "F" : "O";
            }
            if (usage == "M" && !present) findings.Add(new(rule.Field, "Missing", "Mandatory field is absent from this log. " + rule.Comment, ruleId));
            else if (usage == "M" && Value(rule.Field).Length == 0) findings.Add(new(rule.Field, "Invalid", "Mandatory field has an empty logged value.", ruleId));
            else if (usage == "F" && present) findings.Add(new(rule.Field, "Forbidden", rule.Comment, ruleId));
        }
        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var definition = profile.Fields.FirstOrDefault(f => f.Id == field.DataElement);
            if (definition is null) continue;
            var fieldRule = message?.Fields.FirstOrDefault(r => r.Field == definition.Id);
            var variant = fieldRule?.Name;
            if (variant is not null) field = field with { Name = variant };
            if (((fieldRule?.Length ?? definition.Length) is int length && field.Value.Length != length) ||
                (definition.MaxLength is int max && field.Value.Length > max) ||
                (definition.Type == "digits" && !field.Value.All(char.IsAsciiDigit)) ||
                (definition.Type == "hex" && (field.Value.Length % 2 != 0 || !field.Value.All(Uri.IsHexDigit))))
                findings.Add(new(definition.Id, "Invalid", "Logged value does not match the configured type/length.", "field.format"));
            if (definition.Decoder == "emvEntries") field = field with { Children = DecodeIcc(raw, field, findings) };
            if (definition.Decoder is { } decoderId && decoderId != "emvEntries")
            {
                if (!decoders.TryGetValue(decoderId, out var decoder))
                    findings.Add(new(definition.Id, "Undetermined", $"Field decoder '{decoderId}' is unavailable. Enable its plugin and restart.", "decode.unavailable"));
                else
                {
                    try
                    {
                        var values = fields.GroupBy(f => f.DataElement ?? f.Name).ToDictionary(g => g.Key, g => g.First().Value);
                        var decoded = decoder.Decode(new(profile.Id, mti, field, values), token);
                        field = field with { Children = decoded.Children, Specification = decoded.Specification ?? field.Specification };
                        findings.AddRange(decoded.Findings ?? []);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception)
                    {
                        findings.Add(new(definition.Id, "Review", $"Field decoder '{decoderId}' failed; the original value is retained.", "decode.failed"));
                    }
                }
            }
            fields[i] = field;
        }
        var iccAmount = fields.FirstOrDefault(f => f.DataElement == "55")?.Children?.FirstOrDefault(f => f.Name == "9F02")?.Value;
        if (iccAmount is not null && Value("4").Length > 0 && iccAmount != Value("4"))
            findings.Add(new("55", "Review", "ICC authorised amount (9F02) differs from DE4. Check transaction context.", "emv.amount"));
        return new(line, raw, fields)
        {
            Message = new(profile.Id, profile.SpecVersion, header.Groups["record"].Value,
                header.Success ? header.Groups["date"].Value : "unknown",
                header.Success ? header.Groups["direction"].Value : "unknown",
                header.Success ? header.Groups["connection"].Value : "unknown",
                header.Success ? header.Groups["sequence"].Value : "unknown", mti, message?.Role ?? "unknown",
                header.Success ? header.Groups["process"].Value : "unknown"),
            Findings = findings,
            Correlation = GetCorrelation(mti)
        };
    }

    private static SourceRange SliceSource(string raw, SourceRange parent, int offset, int length)
    {
        var start = parent.CharacterOffset + offset;
        return new(parent.ByteOffset + Utf8.GetByteCount(raw.AsSpan(parent.CharacterOffset, offset)),
            Utf8.GetByteCount(raw.AsSpan(start, length)), start, length);
    }

    private static IReadOnlyList<ParsedField> DecodeIcc(string raw, ParsedField field, List<FieldFinding> findings)
    {
        var children = new List<ParsedField>();
        // The decoded value omits log wrapping. Map its characters back to the original text.
        var positions = Enumerable.Range(0, field.Source.CharacterLength)
            .Where(i => !char.IsWhiteSpace(raw[field.Source.CharacterOffset + i])).ToArray();
        SourceRange EntrySource(int offset, int length) => length == 0
            ? SliceSource(raw, field.Source, offset < positions.Length ? positions[offset] : field.Source.CharacterLength, 0)
            : SliceSource(raw, field.Source, positions[offset], positions[offset + length - 1] - positions[offset] + 1);
        foreach (Match match in Regex.Matches(field.Value, @"[^/]+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Cast<Match>().Take(2049))
        {
            var entry = match.Value;
            if (children.Count == 2048) { findings.Add(new("55", "Invalid", "ICC input exceeds 2,048 entries.", "emv.limit")); break; }
            var parts = entry.Split(':');
            try
            {
                if (parts.Length != 3 || parts[0].Length is < 2 or > 6 || parts[0].Length % 2 != 0 ||
                    !parts[0].All(Uri.IsHexDigit) || !int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var length) ||
                    parts[2].Length % 2 != 0 || parts[2].Length / 2 != length || !parts[2].All(Uri.IsHexDigit))
                    throw new FormatException("Malformed tag entry or declared hexadecimal byte length mismatch.");
                var valueSource = EntrySource(match.Index + parts[0].Length + parts[1].Length + 2, parts[2].Length);
                var candidates = EmvDecoder.ProfilesFor(parts[0]);
                var common = candidates.FirstOrDefault(d => d.ProfileId == "emv43");
                if (common is null)
                {
                    // Raw and profile-dependent tags have a value, but no automatic breakdown.
                    children.Add(new(parts[0], parts[2], valueSource));
                    continue;
                }
                var summary = common.Name + "\n" + string.Join("\n",
                    EmvDecoder.DecodeFields(parts[0], parts[2], common.ProfileId).Select(f => $"{f.Name}: {f.Value} — {f.Meaning}")
                    .Concat(common.BitEditor ? EmvDecoder.DecodeBits(parts[0], parts[2], common.ProfileId).Where(b => b.IsSet).Select(b => $"Byte {b.Byte} b{b.Bit}: {b.Meaning}") : []));
                children.Add(new(parts[0], parts[2], valueSource, [new("Breakdown", summary, valueSource)]));
            }
            catch (FormatException ex)
            {
                findings.Add(new("55", "Invalid", ex.Message, "emv.entry"));
                children.Add(new("Malformed ICC entry", entry, EntrySource(match.Index, match.Length)));
            }
        }
        return children;
    }
}
