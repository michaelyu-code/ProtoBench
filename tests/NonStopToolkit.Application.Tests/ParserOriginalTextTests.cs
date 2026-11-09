using NonStopToolkit.Desktop.Features.Parsers;

namespace NonStopToolkit.Application.Tests;

public sealed class ParserOriginalTextTests
{
    [Theory]
    [InlineData("first\r\n\r\n  second\r\n")]
    [InlineData("first\n\n  second\n")]
    [InlineData("first\r\r  second\r")]
    [InlineData("first\r\n\n  second\rlast\n")]
    [InlineData("literal \\r\\n stays literal\nnext")]
    [InlineData("first\u0085second\u2028third\u2029last")]
    public void CopyingAllPreservesExactlyTheSuppliedSeparatorsAndTrailingBreak(string raw)
    {
        var displayed = ParserOriginalText.DisplayText(raw);
        Assert.DoesNotContain('\n', displayed);
        Assert.Equal(raw, ParserOriginalText.SelectedText(raw, 0, displayed.Length));
        // RichEdit includes a final implicit paragraph mark; it must not enter the copied source.
        Assert.Equal(raw, ParserOriginalText.SelectedText(raw, 0, displayed.Length + 1));
    }

    [Fact]
    public void PartialCopyAndSourceHighlightShareTheSameMixedNewlineMapping()
    {
        const string raw = "header\r\n\r\nfield=αβ\nnext\rlast";
        var sourceStart = raw.IndexOf("field=", StringComparison.Ordinal);
        var sourceEnd = raw.IndexOf("last", StringComparison.Ordinal);
        var start = ParserOriginalText.DisplayOffset(raw, sourceStart);
        var end = ParserOriginalText.DisplayOffset(raw, sourceEnd);
        Assert.Equal("field=αβ\nnext\r", ParserOriginalText.SelectedText(raw, start, end));
        Assert.Equal(sourceStart, ParserOriginalText.SourceOffset(raw, start));
        Assert.Equal(sourceEnd, ParserOriginalText.SourceOffset(raw, end));
        Assert.Equal("field=αβ\nnext\r", ParserOriginalText.SelectedText(raw, end, start));
    }
}
