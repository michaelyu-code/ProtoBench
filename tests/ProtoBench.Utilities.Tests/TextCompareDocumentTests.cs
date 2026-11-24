using ProtoBench.Utilities.Text;

namespace ProtoBench.Utilities.Tests;

public sealed class TextCompareDocumentTests
{
    [Fact]
    public void InsertionHasSpacerAndFollowingLinesRemainAligned()
    {
        var document = TextCompareDocument.Create("a\nb\nc", "a\ninserted\nb\nc", new());
        Assert.Null(document.Rows[1].Left);
        Assert.Equal("inserted", document.Rows[1].Right!.Content);
        Assert.Equal("b", document.Rows[2].Left!.Content);
        Assert.Equal("b", document.Rows[2].Right!.Content);
        var change = Assert.Single(document.Changes);
        Assert.Equal(2, change.LeftStart);
        Assert.Equal(0, change.LeftLength);
        Assert.Equal("inserted\n", "a\ninserted\nb\nc".Substring(change.RightStart, change.RightLength));
    }

    [Theory]
    [InlineData("", "a")]
    [InlineData("a", "")]
    [InlineData("a\nb\n", "a\nc\nb\n")]
    [InlineData("same\r\nold🙂\r\nend", "same\r\nnew🙃\r\nend")]
    [InlineData("a", "a\n")]
    [InlineData("a\n\nb", "A\nb")]
    [InlineData("a\u2028b\u0085c", "a\u2028inserted\u0085b\u0085c")]
    public void RangesAndRowsPreserveEverySourceCharacter(string left, string right)
    {
        foreach (var options in new[] { new TextCompareOptions(), new TextCompareOptions(true, true, true) })
        {
            var document = TextCompareDocument.Create(left, right, options);
            Assert.Equal(left, string.Concat(document.Rows.Select(r => r.Left?.Text)));
            Assert.Equal(right, string.Concat(document.Rows.Select(r => r.Right?.Text)));
            foreach (var change in document.Changes)
            {
                var rows = document.Rows.Skip(change.FirstRow).Take(change.RowCount);
                Assert.Equal(left.Substring(change.LeftStart, change.LeftLength), string.Concat(rows.Select(r => r.Left?.Text)));
                Assert.Equal(right.Substring(change.RightStart, change.RightLength), string.Concat(rows.Select(r => r.Right?.Text)));
            }
        }
    }

    [Fact]
    public void RulesSuppressChangesWithoutRewritingInputs()
    {
        const string left = "A b\n\nend";
        const string right = "ab\nend";
        var document = TextCompareDocument.Create(left, right, new(true, true, true));
        Assert.Empty(document.Changes);
        Assert.False(document.IsExactMatch);
        Assert.Equal(left, string.Concat(document.Rows.Select(r => r.Left?.Text)));
        Assert.Equal(right, string.Concat(document.Rows.Select(r => r.Right?.Text)));
    }

    [Fact]
    public void RepeatedTextAndRandomBlankLinesRetainValidCopyRanges()
    {
        var random = new Random(48);
        for (var i = 0; i < 300; i++)
        {
            string Generate() => string.Join('\n', Enumerable.Range(0, random.Next(30)).Select(_ => new[] { "", "a", "b", "A ", "🙂" }[random.Next(5)]));
            var left = Generate(); var right = Generate();
            RangesAndRowsPreserveEverySourceCharacter(left, right);
            var document = TextCompareDocument.Create(left, right, new());
            // Applying all changes from the end must reconstruct the opposite input exactly.
            var copied = left;
            foreach (var change in document.Changes.Reverse())
                copied = copied.Remove(change.LeftStart, change.LeftLength).Insert(change.LeftStart, right.Substring(change.RightStart, change.RightLength));
            Assert.Equal(right, copied);
        }
    }

    [Fact]
    public void BoundedFallbackAndCancellationAreExplicit()
    {
        var document = TextCompareDocument.Create(string.Concat(Enumerable.Repeat("left\n", 2000)), string.Concat(Enumerable.Repeat("right\n", 2000)), new());
        Assert.True(document.IsCoarse);
        Assert.Single(document.Changes);
        Assert.Throws<OperationCanceledException>(() => TextCompareDocument.Create("a", "b", new(), new CancellationToken(true)));
    }
}
