using System.Text;
using ProtoBench.Utilities.Binary;

namespace ProtoBench.Utilities.Text;

public sealed record TextEncodingOption(string Name, int CodePage)
{
    public override string ToString() => Name;

    public Encoding GetEncoding(bool strict = true)
    {
        var encoder = strict ? EncoderFallback.ExceptionFallback : EncoderFallback.ReplacementFallback;
        DecoderFallback decoder = strict ? DecoderFallback.ExceptionFallback : new DecoderReplacementFallback("\uFFFD");
        return CodePage is 1252 or 437
            ? CodePagesEncodingProvider.Instance.GetEncoding(CodePage, encoder, decoder)!
            : Encoding.GetEncoding(CodePage, encoder, decoder);
    }
}

public static class TextEncodings
{
    public static TextEncodingOption Ascii { get; } = new("ASCII (7-bit)", 20127);
    public static TextEncodingOption Utf8 { get; } = new("UTF-8", 65001);
    public static IReadOnlyList<TextEncodingOption> Options { get; } = Array.AsReadOnly(new[]
    {
        Ascii, new("Latin-1 (ISO-8859-1)", 28591), new("Windows-1252", 1252),
        new("OEM CP437", 437), Utf8, new("UTF-16 LE", 1200), new("UTF-16 BE", 1201)
    });

    public static string ToHex(string text, TextEncodingOption encoding)
    {
        if (text.Length > HexBytes.MaxBytes)
            throw new FormatException("Text input is limited to 16,384 characters.");
        try
        {
            var codec = encoding.GetEncoding();
            if (codec.GetByteCount(text) > HexBytes.MaxBytes)
                throw new FormatException("Encoded output is limited to 16,384 bytes.");
            return HexBytes.Format(codec.GetBytes(text));
        }
        catch (EncoderFallbackException ex)
        {
            throw new FormatException($"{encoding.Name} cannot represent the character at position {ex.Index + 1}.", ex);
        }
    }

    public static string FromHex(string hex, TextEncodingOption encoding)
    {
        var bytes = HexBytes.Parse(hex);
        try { return encoding.GetEncoding().GetString(bytes); }
        catch (DecoderFallbackException ex)
        {
            throw new FormatException($"Input contains invalid {encoding.Name} bytes at position {ex.Index + 1}.", ex);
        }
    }
}
