using ProtoBench.Desktop.Features.Text;

namespace ProtoBench.Application.Tests;

public sealed class TextCompareOverviewTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 20_000)]
    [InlineData(100, 39_999)]
    public void ClickingWithinOneLargeChangeTargetsThatPosition(double y, int expectedRow)
    {
        var rows = Enumerable.Repeat((ChangeIndex: 0, Height: 14d), 40_000).ToArray();
        var marks = TextCompareOverview.Create(rows, 100);
        Assert.Equal(new TextCompareOverviewTarget(0, expectedRow), TextCompareOverview.FindTarget(marks, rows, y));
    }

    [Fact]
    public void ClicksMapToAlignedRowsIncludingUnchangedContext()
    {
        (int ChangeIndex, double Height)[] rows = [(-1, 100), (0, 100), (-1, 100), (1, 100), (1, 100)];
        var marks = TextCompareOverview.Create(rows, 100);
        Assert.Equal(new TextCompareOverviewTarget(1, 3), TextCompareOverview.FindTarget(marks, rows, 65));
        Assert.Equal(new TextCompareOverviewTarget(1, 4), TextCompareOverview.FindTarget(marks, rows, 99));
        Assert.Equal(new TextCompareOverviewTarget(0, 1), TextCompareOverview.FindTarget(marks, rows, 0));
    }

    [Fact]
    public void FilteredWrappedRowsUseTheirDisplayedHeightsForNavigation()
    {
        (int ChangeIndex, double Height)[] rows = [(0, 100), (0, 300), (0, 100)];
        var marks = TextCompareOverview.Create(rows, 100);
        Assert.Equal(new TextCompareOverviewTarget(0, 1), TextCompareOverview.FindTarget(marks, rows, 30));
        Assert.Equal(new TextCompareOverviewTarget(0, 2), TextCompareOverview.FindTarget(marks, rows, 85));
    }

    [Fact]
    public void ExpandedTinyMarksClampToTheirSourceRows()
    {
        (int ChangeIndex, double Height)[] rows = [(-1, 1000), (0, 1), (0, 1)];
        var marks = TextCompareOverview.Create(rows, 20);
        Assert.Equal(new TextCompareOverviewTarget(0, 1), TextCompareOverview.FindTarget(marks, rows, 17));
        Assert.Equal(new TextCompareOverviewTarget(0, 2), TextCompareOverview.FindTarget(marks, rows, 30));
        Assert.Null(TextCompareOverview.FindTarget([], [], 0));
        Assert.Null(TextCompareOverview.FindTarget(marks, rows, double.NaN));
    }

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
