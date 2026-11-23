using System.Text;
using ProtoBench.Parsing.Definitions;

namespace ProtoBench.Parsing.Engines;

public sealed record Iso8583Field(int Number, string Description, string Value, string RawValue, int Offset, bool Complete)
{
    public int PrefixByteLength { get; init; }
    public int ValueOffset => Offset + PrefixByteLength;
    public int ValueByteLength => RawValue.Length / 2 - PrefixByteLength;
}
public sealed record Iso8583Result(IReadOnlyList<Iso8583Field> Fields, IReadOnlyList<string> Warnings, string RemainingHex)
{
    public bool IsComplete => Warnings.Count == 0;
}

public static class Iso8583Parser
{
    public static Iso8583Result Parse(Iso8583Profile profile, byte[] data) => Parse(profile, data, Iso8583EbcdicEncoding.Jpos1047);

    public static Iso8583Result Parse(Iso8583Profile profile, byte[] data, Iso8583EbcdicEncoding ebcdicEncoding)
    {
        if (!Enum.IsDefined(ebcdicEncoding)) throw new ArgumentException("Select a supported EBCDIC code page.");
        if (data.Length > 1048576) throw new ArgumentException("Message exceeds 1 MiB.");
        var fields = new List<Iso8583Field>();
        var warnings = new List<string>();
        var offset = 0;
        bool Read(int number)
        {
            if (!profile.Fields.TryGetValue(number, out var definition))
            { warnings.Add($"Field {number}: no XML definition; cannot locate subsequent fields."); return false; }
            var codec = Iso8583Codecs.Get(definition.Class);
            if (codec is null)
            { warnings.Add($"Field {number}: unsupported packager '{definition.Class}'; parsing stopped."); return false; }
            var start = offset;
            var length = definition.Length;
            var prefixBytes = codec.PrefixBytes;
            if (data.Length - offset < prefixBytes)
            {
                fields.Add(new(number, definition.Name, "", Convert.ToHexString(data.AsSpan(start)), start, false) { PrefixByteLength = data.Length - start });
                offset = data.Length;
                warnings.Add($"Incomplete message: field {number} length prefix needs {prefixBytes} bytes.");
                return false;
            }
            if (prefixBytes > 0)
            {
                var prefixData = data.AsSpan(offset, prefixBytes);
                var prefix = codec.Prefix switch
                {
                    IsoPrefix.Bcd => Convert.ToHexString(prefixData),
                    IsoPrefix.Ebcdic => Iso8583Ebcdic.Decode(prefixData, ebcdicEncoding),
                    _ => Encoding.Latin1.GetString(prefixData)
                };
                if (prefix.Any(c => c is < '0' or > '9') || !int.TryParse(prefix, out length) || length > definition.Length || length >= Math.Pow(10, codec.PrefixDigits))
                { warnings.Add($"Field {number}: invalid length prefix or length exceeds XML maximum; parsing stopped."); return false; }
                offset += prefixBytes;
            }
            var bytes = codec.Value switch
            {
                IsoValue.Bcd or IsoValue.PackedHex => (length + 1) / 2,
                IsoValue.BcdAmount => 1 + length / 2,
                IsoValue.AsciiHex or IsoValue.EbcdicHex => length * 2,
                _ => length
            };
            var available = Math.Min(bytes, data.Length - offset);
            var raw = data.AsSpan(offset, available);
            var complete = available == bytes;
            var value = codec.Value switch
            {
                IsoValue.Binary => Convert.ToHexString(raw),
                IsoValue.Ebcdic or IsoValue.EbcdicHex => Iso8583Ebcdic.Decode(raw, ebcdicEncoding),
                IsoValue.Bcd => DecodeBcd(raw, length, definition.Pad, complete),
                IsoValue.PackedHex => DecodeBcd(raw, length, definition.Pad, complete, hexadecimal: true),
                IsoValue.BcdAmount => raw.Length == 0 ? "" : Encoding.Latin1.GetString(raw[..1]) + DecodeBcd(raw[1..], length - 1, definition.Pad, complete),
                _ => Encoding.Latin1.GetString(raw)
            };
            fields.Add(new(number, definition.Name, value, Convert.ToHexString(data.AsSpan(start, prefixBytes + available)), start, complete) { PrefixByteLength = prefixBytes });
            offset += available;
            if (!complete) warnings.Add($"Incomplete message: field {number} needs {bytes} value bytes; only {available} available.");
            if (codec.Value is IsoValue.AsciiHex or IsoValue.EbcdicHex)
            {
                if (value.Any(c => !Uri.IsHexDigit(c)))
                { warnings.Add($"Field {number}: invalid encoded hex value; parsing stopped."); return false; }
                // Binary packagers return bytes, displayed consistently as uppercase hex.
                fields[^1] = fields[^1] with { Value = value.ToUpperInvariant() };
            }
            if (codec.Value is IsoValue.Bcd or IsoValue.PackedHex or IsoValue.BcdAmount && complete && (length & 1) == (codec.Value == IsoValue.BcdAmount ? 0 : 1) && raw.Length > 0)
            {
                var packed = codec.Value == IsoValue.BcdAmount ? raw[1..] : raw;
                var pad = definition.Pad ? packed[0] >> 4 : packed[^1] & 15;
                if (pad != (codec.FPadding && !definition.Pad ? 15 : 0))
                    warnings.Add($"Field {number}: unexpected BCD padding nibble; value retained.");
            }
            return complete;
        }
        Iso8583Result Result() => new(fields, warnings, Convert.ToHexString(data.AsSpan(offset)));
        if (profile.HeaderLength > 0)
        {
            offset = Math.Min(profile.HeaderLength, data.Length);
            fields.Add(new(-1, "Message header", Encoding.Latin1.GetString(data, 0, offset), Convert.ToHexString(data.AsSpan(0, offset)), 0, offset == profile.HeaderLength));
            if (offset < profile.HeaderLength) { warnings.Add("Incomplete message: header is truncated."); return Result(); }
        }
        if (!Read(0)) return Result();
        var bitmapDefinition = profile.Fields[1];
        var encodedBitmap = bitmapDefinition.Class != "org.jpos.iso.IFB_BITMAP";
        var bitmapStart = offset;
        var bitmapBytes = new List<byte>();
        for (var part = 0; part < 3; part++)
        {
            var needed = encodedBitmap ? 16 : 8;
            if (data.Length - offset < needed)
            {
                fields.Add(new(1, bitmapDefinition.Name, Convert.ToHexString(bitmapBytes.ToArray()), Convert.ToHexString(data.AsSpan(bitmapStart)), bitmapStart, false));
                offset = data.Length;
                warnings.Add("Incomplete message: bitmap is truncated; data fields cannot be located.");
                return Result();
            }
            byte[] block;
            try
            {
                var source = data.AsSpan(offset, needed);
                var text = bitmapDefinition.Class == "org.jpos.iso.IFE_BITMAP" ? Iso8583Ebcdic.Decode(source, ebcdicEncoding) : Encoding.Latin1.GetString(source);
                block = encodedBitmap ? Convert.FromHexString(text) : source.ToArray();
            }
            catch (FormatException) { warnings.Add("Invalid encoded hex bitmap; parsing stopped."); return Result(); }
            bitmapBytes.AddRange(block);
            offset += needed;
            if ((block[0] & 128) == 0 || part == 2 || part == 1 && bitmapDefinition.Length == 16) break;
            if (part == 0 && bitmapDefinition.Length == 8)
            {
                fields.Add(new(1, bitmapDefinition.Name, Convert.ToHexString(bitmapBytes.ToArray()), Convert.ToHexString(data.AsSpan(bitmapStart, offset - bitmapStart)), bitmapStart, true));
                warnings.Add("Secondary bitmap indicated but XML bitmap length is 8 (primary only); parsing stopped.");
                return Result();
            }
        }
        fields.Add(new(1, bitmapDefinition.Name, Convert.ToHexString(bitmapBytes.ToArray()), Convert.ToHexString(data.AsSpan(bitmapStart, offset - bitmapStart)), bitmapStart, true));
        for (var number = 2; number <= bitmapBytes.Count * 8; number++)
        {
            if (number == 65 && bitmapDefinition.Length == 24) continue;
            if ((bitmapBytes[(number - 1) / 8] & (128 >> ((number - 1) % 8))) == 0) continue;
            if (number > profile.MaxValidField)
            { warnings.Add($"Field {number} exceeds XML maxValidField; parsing stopped."); return Result(); }
            if (!Read(number)) return Result();
        }
        if (offset < data.Length) warnings.Add($"{data.Length - offset} trailing bytes remain after the declared fields.");
        return Result();
    }

    private static string DecodeBcd(ReadOnlySpan<byte> raw, int digits, bool leftPadded, bool complete, bool hexadecimal = false)
    {
        // jPOS numeric decoding uses uppercase hex digits, with D rendered as the track-2 '=' separator.
        var value = Convert.ToHexString(raw);
        if (!hexadecimal) value = value.Replace('D', '=');
        if ((digits & 1) != 0 && leftPadded && value.Length > 0) value = value[1..];
        if (complete && value.Length > digits) value = value[..digits];
        return value;
    }
}
