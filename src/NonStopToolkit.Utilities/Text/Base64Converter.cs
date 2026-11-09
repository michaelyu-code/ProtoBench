using System.Text;
using NonStopToolkit.Utilities.Binary;

namespace NonStopToolkit.Utilities.Text;

public static class Base64Converter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Encode(string input, bool hexInput = false, bool urlSafe = false)
    {
        if (input.Length > HexBytes.MaxBytes * 4) throw new FormatException("Input is limited to 65,536 characters.");
        byte[] bytes;
        try { bytes = hexInput ? HexBytes.Parse(input) : Utf8.GetBytes(input); }
        catch (EncoderFallbackException) { throw new FormatException("Input contains invalid Unicode text."); }
        if (bytes.Length > HexBytes.MaxBytes) throw new FormatException("Input is limited to 16,384 bytes.");
        var encoded = Convert.ToBase64String(bytes);
        return urlSafe ? encoded.Replace('+', '-').Replace('/', '_').TrimEnd('=') : encoded;
    }

    public static string Decode(string input, bool hexOutput = false, bool urlSafe = false)
    {
        if (input.Length > HexBytes.MaxBytes * 4) throw new FormatException("Base64 input is limited to 65,536 characters.");
        var text = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
        if (text.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '=' || (urlSafe ? c is '-' or '_' : c is '+' or '/'))))
            throw new FormatException("Input contains a character outside the selected Base64 alphabet.");
        var normalized = urlSafe ? text.Replace('-', '+').Replace('_', '/') : text;
        if (urlSafe && !normalized.Contains('=')) normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
        var bytes = Convert.FromBase64String(normalized);
        if (bytes.Length > HexBytes.MaxBytes) throw new FormatException("Decoded data is limited to 16,384 bytes.");
        if (Convert.ToBase64String(bytes) != normalized) throw new FormatException("Base64 must use canonical padding and zero pad bits.");
        if (hexOutput) return Convert.ToHexString(bytes);
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw new FormatException("Decoded bytes are not valid UTF-8. Choose Base64 to hex to inspect binary data."); }
    }
}
