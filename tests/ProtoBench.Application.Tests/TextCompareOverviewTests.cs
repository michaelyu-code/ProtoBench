using ProtoBench.Desktop.Features.Text;

namespace ProtoBench.Application.Tests;

public sealed class TextCompareOverviewTests
{
    [Fact]
    public void FilteredRowsStartAtTheTopWithoutHiddenSourceLines()
    {
        var marks = TextCompareOverview.Create([(0, 26), (0, 26), (1, 26)], 600);
        Assert.Equal(new TextCompareOverviewMark(0, 0, 52), marks[0]);
        Assert.Equal(new TextCompareOverviewMark(1, 52, 26), marks[1]);
    }

    [Fact]
    public void WrappedRowsAndContextUseTheirActualDisplayedHeights()
    {
        var marks = TextCompareOverview.Create([(0, 156), (0, 78), (-1, 26), (1, 52)], 600);
        Assert.Equal(new TextCompareOverviewMark(0, 0, 234), marks[0]);
        Assert.Equal(new TextCompareOverviewMark(1, 260, 52), marks[1]);
        Assert.Equal(0, TextCompareOverview.FindNearest(marks, 200));
        Assert.Equal(1, TextCompareOverview.FindNearest(marks, 280));
    }

    [Fact]
    public void LongComparisonsCompressDrawingAndHitTestingTogether()
    {
        var marks = TextCompareOverview.Create([(0, 200), (-1, 200), (1, 200)], 120);
        Assert.Equal(new TextCompareOverviewMark(0, 0, 40), marks[0]);
        Assert.Equal(new TextCompareOverviewMark(1, 80, 40), marks[1]);
        Assert.Equal(1, TextCompareOverview.FindNearest(marks, 100));
    }

    [Fact]
    public void TinyMarksStayWithinTheStripAndEmptyComparisonsHaveNoTarget()
    {
        var marks = TextCompareOverview.Create([(-1, 1000), (0, 1)], 20);
        var mark = Assert.Single(marks);
        Assert.Equal(3, mark.Height);
        Assert.True(mark.Top + mark.Height <= 20);
        Assert.Empty(TextCompareOverview.Create([], 100));
        Assert.Equal(-1, TextCompareOverview.FindNearest([], 20));
    }
}
