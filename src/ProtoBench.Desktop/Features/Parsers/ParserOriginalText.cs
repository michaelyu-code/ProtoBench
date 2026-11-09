namespace ProtoBench.Desktop.Features.Parsers;

/// <summary>Maps the rich editor's single paragraph marks to the extension's unchanged source text.</summary>
public static class ParserOriginalText
{
    public static string DisplayText(string original) => original.ReplaceLineEndings("\r");

    public static int DisplayOffset(string original, int sourceOffset)
    {
        sourceOffset = Math.Clamp(sourceOffset, 0, original.Length);
        var position = 0;
        for (var i = 0; i < sourceOffset; position++) i += BreakLength(original, i);
        return position;
    }

    public static int SourceOffset(string original, int displayOffset)
    {
        var source = 0;
        for (var position = 0; position < displayOffset && source < original.Length; position++)
            source += BreakLength(original, source);
        return source;
    }

    public static string SelectedText(string original, int displayStart, int displayEnd)
    {
        var start = SourceOffset(original, Math.Min(displayStart, displayEnd));
        var end = SourceOffset(original, Math.Max(displayStart, displayEnd));
        return original[start..end];
    }

    private static int BreakLength(string text, int index) =>
        text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
}
