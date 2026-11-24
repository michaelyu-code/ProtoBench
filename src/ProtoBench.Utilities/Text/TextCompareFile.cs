using System.Text;

namespace ProtoBench.Utilities.Text;

public sealed record TextCompareFile(string Text, Encoding Encoding, string NewLine, bool MixedLineEndings)
{
    public static TextCompareFile Decode(byte[] bytes, Encoding? fallback = null)
    {
        Encoding encoding = fallback ?? new UTF8Encoding(false, true);
        var skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) { encoding = new UTF32Encoding(false, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) { encoding = new UTF32Encoding(true, true, true); skip = 4; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) { encoding = new UTF8Encoding(true, true); skip = 3; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, true, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, true, true); skip = 2; }
        var text = encoding.GetString(bytes, skip, bytes.Length - skip);
        string? newline = null; var mixed = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            var ending = text[i] == '\n' ? "\n" : i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : "\r";
            if (ending == "\r\n") i++;
            newline ??= ending;
            mixed |= newline != ending;
        }
        return new(text, encoding, newline ?? "\n", mixed);
    }

    public static byte[] Encode(string text, Encoding encoding) => encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
    public static string EncodingKey(Encoding encoding) => $"{encoding.CodePage}:{Convert.ToHexString(encoding.GetPreamble())}";
    public static string EncodingName(Encoding encoding) => encoding.CodePage switch
    {
        65001 => encoding.GetPreamble().Length > 0 ? "UTF-8 BOM" : "UTF-8",
        1200 => "UTF-16 LE", 1201 => "UTF-16 BE", 12000 => "UTF-32 LE", 12001 => "UTF-32 BE", 28591 => "Latin-1", _ => encoding.WebName
    };
}
