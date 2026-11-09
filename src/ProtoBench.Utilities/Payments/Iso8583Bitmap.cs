using System.Globalization;
using ProtoBench.Utilities.Binary;

namespace ProtoBench.Utilities.Payments;

public sealed record BitmapResult(string Hex, string Binary, IReadOnlyList<int> Fields, bool HasSecondary);

/// <summary>Primary/secondary bitmap profile: bit 1 signals a secondary bitmap; DE65 is an ordinary field.</summary>
public static class Iso8583Bitmap
{
    public static BitmapResult Decode(string input, bool binary = false)
    {
        byte[] bytes;
        if (binary)
        {
            if (input.Length > 1024) throw new FormatException("Binary input is limited to 1,024 characters.");
            var bits = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
            if (bits.Length is not (64 or 128) || bits.Any(c => c is not ('0' or '1')))
                throw new FormatException("Enter exactly 64 or 128 binary digits; whitespace is allowed.");
            bytes = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        }
        else bytes = HexBytes.Parse(input);
        if (bytes.Length is not (8 or 16)) throw new FormatException("A bitmap must contain 8 or 16 bytes (16 or 32 hex digits).");
        var secondary = (bytes[0] & 0x80) != 0;
        if (secondary != (bytes.Length == 16))
            throw new FormatException("Bit 1 must be set exactly when a 16-byte primary + secondary bitmap is supplied.");
        var fields = Enumerable.Range(2, bytes.Length * 8 - 1).Where(n => (bytes[(n - 1) / 8] & (0x80 >> ((n - 1) % 8))) != 0).ToArray();
        return Result(bytes, fields);
    }

    public static BitmapResult Encode(string input)
    {
        if (input.Length > 1024) throw new FormatException("Field list is limited to 1,024 characters.");
        var fields = new SortedSet<int>();
        foreach (var part in input.Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 2 || n > 128)
                throw new FormatException("Enter field numbers 2–128 separated by commas or whitespace. Bit 1 is set automatically.");
            fields.Add(n);
        }
        var bytes = new byte[fields.Any(n => n > 64) ? 16 : 8];
        if (bytes.Length == 16) bytes[0] = 0x80;
        foreach (var n in fields) bytes[(n - 1) / 8] |= (byte)(0x80 >> ((n - 1) % 8));
        return Result(bytes, fields.ToArray());
    }

    private static BitmapResult Result(byte[] bytes, int[] fields) => new(Convert.ToHexString(bytes),
        string.Join(" ", bytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0'))), Array.AsReadOnly(fields), bytes.Length == 16);
}
