using ProtoBench.Desktop.Features.Text;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Application.Tests;

public sealed class TextCompareVirtualLayoutTests
{
    [Fact]
    public void ThirtyThousandEqualLinesNeedNoDisplaySpacers()
    {
        var text = string.Join('\n', Enumerable.Range(0, 30_000).Select(i => $"<p>line {i}</p>"));
        var document = TextCompareDocument.Create(text, text, new());
        var heights = Enumerable.Repeat(22d, 30_000).ToArray();
        var layout = TextCompareVirtualLayout.Create(document, heights, heights);
        Assert.Empty(layout.Left); Assert.Empty(layout.Right);
        Assert.Equal(30_000, document.Rows.Count);
    }

    [Fact]
    public void InsertionAddsOneWidgetWithoutChangingSourceLines()
    {
        var document = TextCompareDocument.Create("a\nb\nc", "a\ninserted\nb\nc", new());
        var layout = TextCompareVirtualLayout.Create(document, [22, 22, 22], [22, 22, 22, 22]);
        Assert.Equal(new TextCompareLineSpace(1, 22, false), Assert.Single(layout.Left));
        Assert.Empty(layout.Right);
    }

    [Fact]
    public void LeadingAndTrailingGapsStayOutsideSourceText()
    {
        var document = TextCompareDocument.Create("same\n", "start\nsame\nend", new());
        var layout = TextCompareVirtualLayout.Create(document, [22], [22, 22, 22]);
        Assert.Contains(new TextCompareLineSpace(1, 22, true), layout.Left);
        Assert.Contains(new TextCompareLineSpace(1, 22, false), layout.Left);
        Assert.Empty(layout.Right);
        Assert.Equal("same\n", string.Concat(document.Rows.Select(r => r.Left?.Text)));
    }

    [Fact]
    public void WrappedLineHeightsAlignTheFollowingRows()
    {
        var document = TextCompareDocument.Create("old\nsame", "new\nsame", new());
        var layout = TextCompareVirtualLayout.Create(document, [66, 22], [22, 22]);
        Assert.Empty(layout.Left);
        Assert.Equal(new TextCompareLineSpace(1, 44, false), Assert.Single(layout.Right));
    }
}
