using System.Text;
using ProtoBench.Utilities.Binary;

namespace ProtoBench.Utilities.Payments;

public sealed record EmvBit(int Byte, int Bit, bool IsSet, string Meaning);
public sealed record EmvTagDefinition(string Tag, string Name, string[][] Bytes,
    string ProfileId = "emv43", string ProfileName = "EMV contact 4.3 (common)",
    string Reference = "EMV 4.3 Book 3 Annex C / Book 4 Annex A", int MinimumLength = 0, int MaximumLength = 0,
    string Example = "", bool BitEditor = true);
public sealed record EmvDecodedField(string Name, string Value, string Meaning);
public sealed record EmvTlv(string Tag, int Offset, string Value, IReadOnlyList<EmvTlv> Children);

public static partial class EmvDecoder
{
    // Contact EMV 4.3 Book 3 Annex C and Book 4 Annex A2. Arrays run from b8 to b1.
    public static IReadOnlyList<EmvTagDefinition> Definitions { get; } = Array.AsReadOnly(new[]
    {
        new EmvTagDefinition("9F33", "Terminal Capabilities", [
            ["Manual key entry", "Magnetic stripe", "IC with contacts", "RFU", "RFU", "RFU", "RFU", "RFU"],
            ["Plaintext PIN for ICC verification", "Enciphered PIN for online verification", "Signature (paper)", "Enciphered PIN for offline verification", "No CVM required", "RFU", "RFU", "RFU"],
            ["SDA", "DDA", "Card capture", "RFU", "CDA", "RFU", "RFU", "RFU"]]),
        new EmvTagDefinition("95", "Terminal Verification Results (TVR)", [
            ["Offline data authentication was not performed", "SDA failed", "ICC data missing", "Card appears on terminal exception file", "DDA failed", "CDA failed", "RFU", "RFU"],
            ["ICC and terminal have different application versions", "Expired application", "Application not yet effective", "Requested service not allowed for card product", "New card", "RFU", "RFU", "RFU"],
            ["Cardholder verification was not successful", "Unrecognised CVM", "PIN Try Limit exceeded", "PIN entry required and PIN pad not present or not working", "PIN entry required, PIN pad present, but PIN was not entered", "Online PIN entered", "RFU", "RFU"],
            ["Transaction exceeds floor limit", "Lower consecutive offline limit exceeded", "Upper consecutive offline limit exceeded", "Transaction selected randomly for online processing", "Merchant forced transaction online", "RFU", "RFU", "RFU"],
            ["Default TDOL used", "Issuer authentication failed", "Script processing failed before final GENERATE AC", "Script processing failed after final GENERATE AC", "RFU", "RFU", "RFU", "RFU"]]),
        new EmvTagDefinition("9B", "Transaction Status Information (TSI)", [
            ["Offline data authentication was performed", "Cardholder verification was performed", "Card risk management was performed", "Issuer authentication was performed", "Terminal risk management was performed", "Script processing was performed", "RFU", "RFU"],
            ["RFU", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU"]]),
        new EmvTagDefinition("82", "Application Interchange Profile (AIP)", [
            ["RFU", "SDA supported", "DDA supported", "Cardholder verification supported", "Terminal risk management is to be performed", "Issuer authentication supported", "RFU", "CDA supported"],
            ["Reserved for EMV Contactless Specifications", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU", "RFU"]])
    }.Concat(AdditionalDefinitions()).ToArray());

    public static IReadOnlyList<EmvTagDefinition> Profiles => Definitions.Concat(SchemeDefinitions()).ToArray();
    public static IReadOnlyList<EmvTagDefinition> ProfilesFor(string tag) => Profiles.Where(d => d.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)).ToArray();
    public static EmvTagDefinition Definition(string tag, string? profileId = null) =>
        Profiles.FirstOrDefault(d => d.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase) && (profileId is null || d.ProfileId == profileId))
        ?? throw new FormatException("This tag/profile combination is not supported. Choose one of the listed profiles.");

    public static IReadOnlyList<EmvBit> DecodeBits(string tag, string value, string? profileId = null)
    {
        var definition = Definition(tag, profileId);
        var bytes = ReadValue(definition, value);
        var rows = BitRows(definition, bytes);
        return bytes.SelectMany((b, i) => Enumerable.Range(0, 8).Select(j =>
            new EmvBit(i + 1, 8 - j, (b & (0x80 >> j)) != 0, rows[i][j]))).ToArray();
    }

    public static string FormatBits(string tag, string value, string? profileId = null)
    {
        var definition = Definition(tag, profileId);
        var bits = DecodeBits(tag, value, profileId);
        var fields = DecodeFields(tag, value, profileId);
        var summary = fields.Count == 0 ? "" : string.Join("\n", fields.Select(f => $"{f.Name}: {f.Value} — {f.Meaning}")) + "\n";
        if (!definition.BitEditor) return $"{tag.ToUpperInvariant()} — {definition.Name}\nProfile: {definition.ProfileName}\n{summary}".TrimEnd();
        return $"{tag.ToUpperInvariant()} — {definition.Name}\nProfile: {definition.ProfileName}\n{summary}" + string.Join("\n", bits.Select(b =>
            $"Byte {b.Byte} · b{b.Bit} = {(b.IsSet ? 1 : 0)}  {b.Meaning}{(b.IsSet && b.Meaning == "RFU" ? " (reserved bit is set)" : "")}"));
    }

    public static string SetBit(string tag, string value, int byteNumber, int bit, bool isSet, string? profileId = null)
    {
        // Validate the tag and its exact value length before editing a byte.
        DecodeBits(tag, value, profileId);
        var bytes = HexBytes.Parse(value);
        if (byteNumber < 1 || byteNumber > bytes.Length) throw new ArgumentOutOfRangeException(nameof(byteNumber));
        if (bit < 1 || bit > 8) throw new ArgumentOutOfRangeException(nameof(bit));
        var mask = 1 << (bit - 1);
        bytes[byteNumber - 1] = (byte)(isSet ? bytes[byteNumber - 1] | mask : bytes[byteNumber - 1] & ~mask);
        return Convert.ToHexString(bytes);
    }

    public static IReadOnlyList<EmvTlv> ParseTlv(string input)
    {
        var bytes = HexBytes.Parse(input);
        if (bytes.Length == 0) throw new FormatException("Enter at least one complete BER-TLV object.");
        var count = 0;
        IReadOnlyList<EmvTlv> Parse(int start, int end, int depth)
        {
            if (depth > 16) throw new FormatException("TLV nesting is limited to 16 levels.");
            var nodes = new List<EmvTlv>();
            var p = start;
            while (p < end)
            {
                if (++count > 2048) throw new FormatException("TLV input is limited to 2,048 objects.");
                var offset = p;
                var first = bytes[p++];
                if (first is 0 or 0xFF) throw new FormatException($"Invalid tag at byte {offset}. Padding bytes are not accepted.");
                if ((first & 0x1F) == 0x1F)
                {
                    var tagLength = 1;
                    byte next;
                    do
                    {
                        if (p >= end || tagLength == 3) throw new FormatException($"Truncated or oversized tag at byte {offset}.");
                        next = bytes[p++];
                        if (tagLength == 1 && (next & 0x7F) == 0) throw new FormatException($"Invalid tag at byte {offset}.");
                        tagLength++;
                    } while ((next & 0x80) != 0);
                }
                var tag = Convert.ToHexString(bytes.AsSpan(offset, p - offset));
                if (p >= end) throw new FormatException($"Missing length for tag {tag} at byte {offset}.");
                var length = (int)bytes[p++];
                if ((length & 0x80) != 0)
                {
                    var size = length & 0x7F;
                    if (size is 0 or > 3 || p + size > end) throw new FormatException($"Invalid or indefinite length for tag {tag}.");
                    length = 0;
                    for (var i = 0; i < size; i++) length = (length << 8) | bytes[p++];
                }
                if (length > end - p) throw new FormatException($"Value of tag {tag} extends past its container at byte {offset}.");
                var children = (first & 0x20) != 0 ? Parse(p, p + length, depth + 1) : Array.Empty<EmvTlv>();
                nodes.Add(new(tag, offset, Convert.ToHexString(bytes.AsSpan(p, length)), children));
                p += length;
            }
            return nodes.AsReadOnly();
        }
        return Parse(0, bytes.Length, 0);
    }

    public static string FormatTlv(string input, string? schemeProfileId = null)
    {
        var output = new StringBuilder();
        void Append(IReadOnlyList<EmvTlv> nodes, int depth)
        {
            foreach (var node in nodes)
            {
                var candidates = ProfilesFor(node.Tag);
                var context = schemeProfileId == "visa" ? node.Tag == "9F6E" ? "visa-ffi-v1" : "visa-k3" : schemeProfileId;
                var selected = candidates.FirstOrDefault(d => d.ProfileId == context);
                // Ambiguous tags require an explicit scheme; common definitions remain usable without one.
                selected ??= candidates.FirstOrDefault(d => d.ProfileId == "emv43");
                var name = selected?.Name ?? (candidates.Count > 0 ? "Scheme-specific tag (choose an explicit tag/profile to decode)" : node.Tag switch
                {
                    "70" => "EMV record template", "77" => "Response message template", "6F" => "FCI template",
                    "A5" => "FCI proprietary template", "84" => "DF name", "5A" => "Application PAN", "57" => "Track 2 equivalent data",
                    "9F26" => "Application cryptogram", "9F27" => "Cryptogram information data", "9F36" => "Application transaction counter",
                    "9F37" => "Unpredictable number", "9F02" => "Amount, authorised", "9F03" => "Amount, other",
                    "9F10" => "Issuer application data", "9F34" => "CVM results", "5F2A" => "Transaction currency code",
                    "9A" => "Transaction date", "9C" => "Transaction type", _ => "Unknown / profile-specific tag"
                });
                var indent = new string(' ', depth * 2);
                output.AppendLine($"{indent}{node.Tag} — {name} [byte {node.Offset}, {node.Value.Length / 2} bytes]");
                if (node.Children.Count > 0) Append(node.Children, depth + 1);
                else
                {
                    output.AppendLine($"{indent}  {node.Value}");
                    var definition = selected;
                    if (definition is not null)
                    {
                        // A known tag with a bad value length is invalid, even inside a valid TLV container.
                        foreach (var field in DecodeFields(node.Tag, node.Value, definition.ProfileId))
                            output.AppendLine($"{indent}  {field.Name}: {field.Value} — {field.Meaning}");
                        foreach (var bit in (definition.BitEditor ? DecodeBits(node.Tag, node.Value, definition.ProfileId) : []).Where(b => b.IsSet))
                            output.AppendLine($"{indent}  Byte {bit.Byte} b{bit.Bit}: {bit.Meaning}");
                    }
                }
            }
        }
        Append(ParseTlv(input), 0);
        return output.ToString().TrimEnd();
    }
}
