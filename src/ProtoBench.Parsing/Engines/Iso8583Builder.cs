using System.Globalization;
using System.Text;
using ProtoBench.Parsing.Definitions;

namespace ProtoBench.Parsing.Engines;

public enum Iso8583ValueInput { Text, Hex }
public sealed record Iso8583BuildField(int Number, string Value, Iso8583ValueInput Input = Iso8583ValueInput.Text, int? DigitCount = null);
public sealed record Iso8583BuildResult(byte[] Bytes, Iso8583Result Parsed)
{
    public string Hex => Convert.ToHexString(Bytes);
    public string AsciiPreview => string.Concat(Bytes.Select(b => b is >= 32 and <= 126 ? ((char)b).ToString() : $"\\x{b:X2}"));
    public bool IsPrintableAscii => Bytes.All(b => b is >= 32 and <= 126);
}

/// <summary>Packs the same declarative field codecs as Iso8583Parser; never executes Java packagers.</summary>
public static class Iso8583Builder
{
    public static bool NeedsHexDigitCount(Iso8583FieldDefinition field)
        => Iso8583Codecs.Get(field.Class) is { PrefixDigits: > 0, Value: IsoValue.Bcd };

    public static Iso8583BuildResult Build(Iso8583Profile profile, IEnumerable<Iso8583BuildField> inputs,
        Iso8583EbcdicEncoding ebcdicEncoding = Iso8583EbcdicEncoding.Jpos1047, string headerHex = "",
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(ebcdicEncoding)) throw new ArgumentException("Select a supported EBCDIC code page.");
        var fields = inputs.Take(194).ToArray();
        if (fields.Length > 192) throw new FormatException("Too many fields.");
        if (fields.GroupBy(f => f.Number).Any(g => g.Count() > 1)) throw new FormatException("Each field may be added only once.");
        if (!fields.Any(f => f.Number == 0)) throw new FormatException("Field 0 (MTI) is required.");
        var bitmapDefinition = profile.Fields[1];
        foreach (var field in fields)
        {
            if (field.Number == 1 || field.Number == 65 && bitmapDefinition.Length == 24)
                throw new FormatException($"Field {field.Number} is an automatically generated bitmap indicator.");
            if (field.Number < 0 || field.Number > profile.MaxValidField || !profile.Fields.ContainsKey(field.Number))
                throw new FormatException($"Field {field.Number} is not available in the selected XML profile.");
        }
        var highest = fields.Max(f => f.Number);
        var bitmapLength = highest > 128 ? 24 : highest > 64 ? 16 : 8;
        if (bitmapLength > bitmapDefinition.Length) throw new FormatException("The selected fields exceed the XML bitmap capacity.");
        var bitmap = new byte[bitmapLength];
        void SetBit(int number) => bitmap[(number - 1) / 8] |= (byte)(128 >> ((number - 1) % 8));
        foreach (var field in fields.Where(f => f.Number > 1)) SetBit(field.Number);
        if (bitmapLength > 8) SetBit(1);
        if (bitmapLength > 16) SetBit(65);

        var header = ParseHex(headerHex);
        if (header.Length != profile.HeaderLength) throw new FormatException($"Header requires exactly {profile.HeaderLength} bytes, supplied as hex.");
        using var message = new MemoryStream();
        void Write(byte[] bytes)
        {
            if (message.Length + bytes.Length > 1048576) throw new FormatException("Built message exceeds 1 MiB.");
            message.Write(bytes);
        }
        Write(header);
        foreach (var field in fields.OrderBy(f => f.Number))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { Write(Pack(profile.Fields[field.Number], field, ebcdicEncoding)); }
            catch (Exception ex) when (ex is FormatException or EncoderFallbackException)
            { throw new FormatException($"Field {field.Number} ({profile.Fields[field.Number].Name}): {ex.Message}", ex); }
            if (field.Number == 0)
                Write(bitmapDefinition.Class switch
                {
                    "org.jpos.iso.IFA_BITMAP" => Encoding.ASCII.GetBytes(Convert.ToHexString(bitmap)),
                    "org.jpos.iso.IFE_BITMAP" => Iso8583Ebcdic.Encode(Convert.ToHexString(bitmap), ebcdicEncoding),
                    _ => bitmap
                });
        }
        var data = message.ToArray();
        var parsed = Iso8583Parser.Parse(profile, data, ebcdicEncoding);
        if (!parsed.IsComplete) throw new FormatException("Message validation failed: " + string.Join(" ", parsed.Warnings));
        return new(data, parsed);
    }

    private static byte[] Pack(Iso8583FieldDefinition definition, Iso8583BuildField field, Iso8583EbcdicEncoding encoding)
    {
        var codec = Iso8583Codecs.Get(definition.Class) ?? throw new FormatException($"Unsupported packager '{definition.Class}'.");
        if (!Enum.IsDefined(field.Input)) throw new FormatException("Choose text or hex input.");
        if (field.Value.Length > 262144) throw new FormatException("Value is too large.");
        byte[] value;
        int length;
        if (field.Input == Iso8583ValueInput.Hex)
        {
            value = ParseHex(field.Value);
            length = codec.Value switch
            {
                IsoValue.Bcd or IsoValue.PackedHex => codec.PrefixDigits == 0 ? definition.Length : field.DigitCount ?? value.Length * 2,
                IsoValue.BcdAmount => definition.Length,
                IsoValue.AsciiHex or IsoValue.EbcdicHex => value.Length / 2,
                _ => value.Length
            };
            var expected = codec.Value switch
            {
                IsoValue.Bcd or IsoValue.PackedHex => (length + 1) / 2,
                IsoValue.BcdAmount => 1 + length / 2,
                IsoValue.AsciiHex or IsoValue.EbcdicHex => length * 2,
                _ => length
            };
            if (length < 0 || expected != value.Length) throw new FormatException("Hex value byte count does not match the field/digit length.");
            if (codec.Value is IsoValue.Bcd or IsoValue.PackedHex && codec.PrefixDigits > 0 &&
                (length > value.Length * 2 || length < value.Length * 2 - 1))
                throw new FormatException("BCD digit count must use all value bytes, excluding only one padding nibble.");
            if (codec.Value is IsoValue.AsciiHex or IsoValue.EbcdicHex)
            {
                var hexText = codec.Value == IsoValue.AsciiHex ? Encoding.Latin1.GetString(value) : Iso8583Ebcdic.Decode(value, encoding);
                if (hexText.Any(c => !Uri.IsHexDigit(c))) throw new FormatException("Encoded binary value must contain hex characters.");
            }
        }
        else
        {
            if (field.Value.Any(c => c > 127)) throw new FormatException("Text entry accepts ASCII characters. Use encoded hex for other characters.");
            var text = field.Value;
            var numeric = field.Number == 0 || definition.Class.EndsWith("NUMERIC", StringComparison.Ordinal) || definition.Class.EndsWith("NUM", StringComparison.Ordinal);
            var amount = definition.Class.EndsWith("_AMOUNT", StringComparison.Ordinal);
            if (numeric && (text.Length == 0 || text.Any(c => !char.IsAsciiDigit(c) && !(codec.PrefixDigits > 0 && c == '='))))
                throw new FormatException("Numeric values require digits (track-2 '=' is allowed in variable numeric fields).");
            if (amount && (text.Length < 2 || !"CD+-".Contains(text[0]) || text[1..].Any(c => !char.IsAsciiDigit(c))))
                throw new FormatException("Amount requires a C, D, + or - sign followed by digits.");
            if (codec.Value == IsoValue.PackedHex && text.Any(c => !Uri.IsHexDigit(c))) throw new FormatException("Packed hex value requires hex characters.");
            var binary = codec.Value is IsoValue.Binary or IsoValue.AsciiHex or IsoValue.EbcdicHex;
            if (!binary && codec.PrefixDigits == 0)
            {
                if (text.Length > definition.Length) throw new FormatException($"Value exceeds fixed length {definition.Length}.");
                if (definition.Trim && text.Length != definition.Length) throw new FormatException($"Fixed trim=true fields require exactly {definition.Length} characters for a complete message.");
                text = amount ? text[..1] + text[1..].PadLeft(definition.Length - 1, '0')
                    : numeric || codec.Value == IsoValue.PackedHex ? text.PadLeft(definition.Length, '0') : text.PadRight(definition.Length, ' ');
            }
            length = text.Length;
            value = codec.Value switch
            {
                IsoValue.Ebcdic => Iso8583Ebcdic.Encode(text, encoding),
                IsoValue.Bcd or IsoValue.PackedHex => PackDigits(text, definition.Pad, codec.FPadding),
                IsoValue.BcdAmount => [(byte)text[0], .. PackDigits(text[1..], definition.Pad, false)],
                IsoValue.AsciiHex => Encoding.ASCII.GetBytes(Convert.ToHexString(Encoding.ASCII.GetBytes(text))),
                IsoValue.EbcdicHex => Iso8583Ebcdic.Encode(Convert.ToHexString(Encoding.ASCII.GetBytes(text)), encoding),
                _ => Encoding.ASCII.GetBytes(text)
            };
        }
        if (length > definition.Length || codec.PrefixDigits > 0 && length >= Math.Pow(10, codec.PrefixDigits))
            throw new FormatException($"Value length {length} exceeds the XML or length-prefix maximum.");
        if (codec.PrefixDigits == 0 && length != definition.Length) throw new FormatException($"Fixed field requires exactly {definition.Length} value units.");
        if (field.Number == 0 && (field.Input == Iso8583ValueInput.Text && field.Value.Length != definition.Length))
            throw new FormatException($"Enter the complete {definition.Length}-digit MTI.");
        if (field.Input == Iso8583ValueInput.Hex) ValidateEncodedValue(definition, codec, value, length, encoding);
        if (codec.PrefixDigits == 0) return value;
        var prefix = length.ToString(CultureInfo.InvariantCulture).PadLeft(codec.PrefixDigits, '0');
        var prefixBytes = codec.Prefix switch
        {
            IsoPrefix.Bcd => Convert.FromHexString(prefix.PadLeft(codec.PrefixBytes * 2, '0')),
            IsoPrefix.Ebcdic => Iso8583Ebcdic.Encode(prefix, encoding),
            _ => Encoding.ASCII.GetBytes(prefix)
        };
        return [.. prefixBytes, .. value];
    }

    private static void ValidateEncodedValue(Iso8583FieldDefinition definition, Iso8583Codec codec, byte[] value, int length, Iso8583EbcdicEncoding encoding)
    {
        var numeric = definition.Id == 0 || definition.Class.EndsWith("NUMERIC", StringComparison.Ordinal) || definition.Class.EndsWith("NUM", StringComparison.Ordinal);
        var amount = definition.Class.EndsWith("_AMOUNT", StringComparison.Ordinal);
        if (!numeric && !amount) return;
        string decoded;
        if (codec.Value is IsoValue.Bcd or IsoValue.BcdAmount)
        {
            var packed = codec.Value == IsoValue.BcdAmount ? value.AsSpan(1) : value.AsSpan();
            var digits = codec.Value == IsoValue.BcdAmount ? length - 1 : length;
            decoded = Convert.ToHexString(packed).Replace('D', '=');
            if ((digits & 1) != 0) decoded = definition.Pad ? decoded[1..] : decoded[..^1];
            if (codec.Value == IsoValue.BcdAmount) decoded = ((char)value[0]) + decoded;
        }
        else decoded = codec.Value == IsoValue.Ebcdic ? Iso8583Ebcdic.Decode(value, encoding) : Encoding.Latin1.GetString(value);
        if (numeric && (decoded.Length == 0 || decoded.Any(c => !char.IsAsciiDigit(c) && !(codec.PrefixDigits > 0 && definition.Id != 0 && c == '='))))
            throw new FormatException("Hex bytes do not encode a valid numeric value for this packager.");
        if (amount && (decoded.Length < 2 || !"CD+-".Contains(decoded[0]) || decoded[1..].Any(c => !char.IsAsciiDigit(c))))
            throw new FormatException("Hex amount must encode a C, D, + or - sign followed by digits.");
    }

    private static byte[] PackDigits(string digits, bool left, bool fPadding)
    {
        var hex = digits.Replace('=', 'D');
        if ((hex.Length & 1) != 0) hex = left ? "0" + hex : hex + (fPadding ? "F" : "0");
        return Convert.FromHexString(hex);
    }

    private static byte[] ParseHex(string text)
    {
        try { return Convert.FromHexString(string.Concat(text.Where(c => !char.IsWhiteSpace(c)))); }
        catch (FormatException ex) { throw new FormatException("Enter complete hex byte pairs (whitespace is allowed).", ex); }
    }
}
