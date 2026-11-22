using System.Text;

namespace ProtoBench.Utilities.Text;

public sealed record TextCleanupOptions
{
    public bool Spaces { get; init; }
    public bool Tabs { get; init; }
    public bool LineEndings { get; init; }
    public bool AllWhitespace { get; init; }
    public bool Digits { get; init; }
    public bool Punctuation { get; init; }
    public bool ControlCharacters { get; init; }
    public string CustomCharacters { get; init; } = "";
}

public sealed record TextCleanupResult(string Text, int RemovedCharacters);

public static class TextCleanup
{
    public const int MaxCharacters = 1_000_000;

    public static TextCleanupResult Clean(string input, TextCleanupOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        if (input.Length > MaxCharacters || options.CustomCharacters.Length > MaxCharacters)
            throw new FormatException("Text cleanup is limited to 1,000,000 characters.");
        cancellationToken.ThrowIfCancellationRequested();
        var custom = options.CustomCharacters.EnumerateRunes().ToHashSet();
        var output = new StringBuilder(input.Length);
        var removed = 0;
        var processed = 0;
        var offset = 0;
        foreach (var rune in input.EnumerateRunes())
        {
            if ((processed++ & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var lineEnding = rune.Value is '\r' or '\n' or 0x85 or 0x2028 or 0x2029;
            var remove = custom.Contains(rune)
                || options.Spaces && rune.Value == ' '
                || options.Tabs && rune.Value == '\t'
                || options.LineEndings && lineEnding
                || options.AllWhitespace && Rune.IsWhiteSpace(rune)
                || options.Digits && Rune.IsDigit(rune)
                || options.Punctuation && Rune.IsPunctuation(rune)
                || options.ControlCharacters && Rune.IsControl(rune) && rune.Value != '\t' && !lineEnding;
            if (remove) removed++;
            else output.Append(input.AsSpan(offset, rune.Utf16SequenceLength));
            offset += rune.Utf16SequenceLength;
        }
        return new(output.ToString(), removed);
    }
}
