using System.Text;
using NonStopToolkit.Utilities.Binary;

namespace NonStopToolkit.Utilities.Text;

public interface IAsciiHexConverter
{
    string ToHex(string ascii);
    string ToAscii(string hex);
}

/// <summary>Strict 7-bit ASCII conversion, preserving whitespace and control characters.</summary>
public sealed class AsciiHexConverter : IAsciiHexConverter
{
    public const int MaxBytes = 16384;

    public string ToHex(string ascii)
    {
        ArgumentNullException.ThrowIfNull(ascii);
        if (ascii.Length > MaxBytes)
            throw new FormatException("ASCII input is limited to 16,384 characters.");
        var bytes = new byte[ascii.Length];
        for (var i = 0; i < ascii.Length; i++)
        {
            if (ascii[i] > 127)
                throw new FormatException($"Character at position {i + 1} is outside ASCII (00–7F).");
            bytes[i] = (byte)ascii[i];
        }
        return HexBytes.Format(bytes);
    }

    public string ToAscii(string hex)
    {
        var text = new StringBuilder();
        foreach (var value in HexBytes.Parse(hex))
        {
            if (value > 127)
                throw new FormatException($"Byte {value:X2} is outside ASCII (00–7F).");
            text.Append((char)value);
        }
        return text.ToString();
    }
}
