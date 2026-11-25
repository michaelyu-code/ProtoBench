using ProtoBench.Desktop.Features.Text;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Application.Tests;

public sealed class TextCompareSpacingTests
{
    [Theory]
    [InlineData(200, 0)]
    [InlineData(200, -4200)]
    [InlineData(10_000, 0)]
    [InlineData(10_000, -239_600)]
    public void TailSpaceIsIndependentOfScrollPosition(int lines, double origin)
    {
        var height = lines * 24d;
        // A full pane needs no tail, even when its origin is far above the viewport.
        Assert.Equal(0, TextCompareSpacing.TailSpace(height, origin + height - 24, origin, 24));
        // The empty pane needs a scrollable gap after its implicit empty paragraph.
        Assert.Equal(height - 24, TextCompareSpacing.TailSpace(height, origin, origin, 24));
        Assert.Equal(0, TextCompareSpacing.TailSpace(0, origin, origin, 24));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(10_000)]
    public void UnchangedAndReplacedLinesNeedNoParagraphFormatting(int lines)
    {
        var original = string.Join('\n', Enumerable.Range(0, lines).Select(i => $"line {i}"));
        foreach (var modified in new[] { original, original.Replace("line", "changed") })
        {
            var document = TextCompareDocument.Create(original, modified, new());
            var spacing = TextCompareSpacing.Create(document, 24, 24);
            Assert.Empty(spacing.Left);
            Assert.Empty(spacing.Right);
            Assert.Equal(lines * 24, spacing.Height);
        }
    }

    [Theory]
    [InlineData(200)]
    [InlineData(10_000)]
    public void InsertionOnlyFormatsTheFollowingParagraph(int lines)
    {
        var original = string.Join('\n', Enumerable.Range(0, lines).Select(i => $"line {i}"));
        var modified = original.Replace("line 100\n", "inserted\nline 100\n");
        var document = TextCompareDocument.Create(original, modified, new());
        var spacing = TextCompareSpacing.Create(document, 24, 24);
        var gap = Assert.Single(spacing.Left);
        Assert.Equal(101, gap.Line.Number);
        Assert.Equal(24, gap.Pixels);
        Assert.Empty(spacing.Right);
        Assert.Equal((lines + 1) * 24, spacing.Height);
    }

    [Theory]
    [InlineData("", "a\nb\nc")]
    [InlineData("a\nb\nc", "")]
    [InlineData("a\nc\ne", "a\nb\nc\nd\ne")]
    [InlineData("a\nb\nc\nd\ne", "a\nc\ne")]
    [InlineData("a\n", "a\nb\n")]
    public void PlannedGapsAlignEverySourceRow(string left, string right)
    {
        var document = TextCompareDocument.Create(left, right, new());
        var spacing = TextCompareSpacing.Create(document, 24, 24);
        var leftGaps = spacing.Left.ToDictionary(s => s.Line.Number, s => s.Pixels);
        var rightGaps = spacing.Right.ToDictionary(s => s.Line.Number, s => s.Pixels);
        var leftY = 0d; var rightY = 0d; var rowY = 0d;
        foreach (var row in document.Rows)
        {
            if (row.Left is { } a)
            {
                leftY += leftGaps.GetValueOrDefault(a.Number);
                Assert.Equal(rowY, leftY);
                leftY += 24;
            }
            if (row.Right is { } b)
            {
                rightY += rightGaps.GetValueOrDefault(b.Number);
                Assert.Equal(rowY, rightY);
                rightY += 24;
            }
            rowY += 24;
        }
        Assert.Equal(rowY, spacing.Height);
    }
}
