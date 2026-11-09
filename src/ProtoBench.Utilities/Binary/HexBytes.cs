namespace ProtoBench.Utilities.Binary;

/// <summary>Shared byte-pair syntax for text encodings, with bounded input and output.</summary>
public static class HexBytes
{
    public const int MaxBytes = 16384;

    public static string Format(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxBytes)
            throw new FormatException("Output is limited to 16,384 bytes.");
        return string.Join(" ", bytes.ToArray().Select(b => b.ToString("X2")));
    }

    public static byte[] Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        if (hex.Length > MaxBytes * 4)
            throw new FormatException("Hex input is limited to 65,536 characters.");
        var bytes = new List<byte>();
        int? highNibble = null;
        for (var i = 0; i < hex.Length; i++)
        {
            var c = hex[i];
            if (char.IsWhiteSpace(c))
            {
                if (highNibble is not null)
                    throw new FormatException("Each hex byte needs two adjacent digits; whitespace is allowed between bytes.");
                continue;
            }
            var digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'F' => c - 'A' + 10,
                >= 'a' and <= 'f' => c - 'a' + 10,
                _ => throw new FormatException($"Invalid hex digit at position {i + 1}. Use byte pairs such as 41 42 or 4142.")
            };
            if (highNibble is null)
            {
                highNibble = digit;
                continue;
            }
            if (bytes.Count >= MaxBytes)
                throw new FormatException("Decoded input is limited to 16,384 bytes.");
            bytes.Add((byte)(highNibble.Value * 16 + digit));
            highNibble = null;
        }
        if (highNibble is not null)
            throw new FormatException("Hex input must contain complete two-digit bytes.");
        return bytes.ToArray();
    }
}
