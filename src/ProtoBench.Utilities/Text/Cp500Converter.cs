using System.Text;
using ProtoBench.Utilities.Binary;

namespace ProtoBench.Utilities.Text;

public interface ICp500Converter
{
    byte[] Encode(string text);
    string Decode(ReadOnlySpan<byte> bytes);
    string ToHex(string text);
    string ToText(string hex);
}

/// <summary>IBM EBCDIC International (CP500), with no best-fit or replacement fallback.</summary>
public sealed class Cp500Converter : ICp500Converter
{
    // Obtain only this encoding directly; do not globally register or change default encodings.
    private static readonly Encoding Encoding = CodePagesEncodingProvider.Instance.GetEncoding(
        500, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        ?? throw new InvalidOperationException("CP500 encoding is unavailable.");

    public byte[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > HexBytes.MaxBytes)
            throw new FormatException("CP500 input is limited to 16,384 characters.");
        try
        {
            return Encoding.GetBytes(text);
        }
        catch (EncoderFallbackException ex)
        {
            throw new FormatException($"Text contains a character that CP500 cannot represent at position {ex.Index + 1}.", ex);
        }
    }

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > HexBytes.MaxBytes)
            throw new FormatException("CP500 input is limited to 16,384 bytes.");
        return Encoding.GetString(bytes);
    }

    public string ToHex(string text) => HexBytes.Format(Encode(text));
    public string ToText(string hex) => Decode(HexBytes.Parse(hex));
}
