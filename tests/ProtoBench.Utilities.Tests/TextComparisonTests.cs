using ProtoBench.Utilities.Text;

namespace ProtoBench.Utilities.Tests;

public sealed class TextComparisonTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("same\r\ntext🙂", "same\r\ntext🙂")]
    public void IdenticalTextHasNoChanges(string original, string modified)
    {
        var result = TextComparison.Compare(original, modified);
        Assert.True(result.AreEqual);
        Assert.False(result.HasCoarseChanges);
        AssertReconstructs(original, modified, result);
    }

    [Theory]
    [InlineData("hello cat!", "hello dog!", "cat", "dog")]
    [InlineData("a\r\nb\r\nc", "a\r\nB\r\nc", "b", "B")]
    [InlineData("abc", "abXYZc", "", "XYZ")]
    [InlineData("abXYZc", "abc", "XYZ", "")]
    [InlineData("", "new\ntext", "", "new\ntext")]
    [InlineData("old\ntext", "", "old\ntext", "")]
    [InlineData("a b\t", "ab\t", " ", "")]
    [InlineData("a\r\nb", "a\nb", "\r\n", "\n")]
    [InlineData("A🙂éZ", "A🙃èZ", "🙂é", "🙃è")]
    public void HighlightsChangesAndPreservesText(string original, string modified, string removed, string added)
    {
        var result = TextComparison.Compare(original, modified);
        Assert.False(result.AreEqual);
        Assert.False(result.HasCoarseChanges);
        Assert.Equal(removed, Join(result, TextDifferenceKind.Removed));
        Assert.Equal(added, Join(result, TextDifferenceKind.Added));
        AssertReconstructs(original, modified, result);
    }

    [Fact]
    public void InsertedLinesDoNotMarkFollowingLinesAsChanged()
    {
        var original = "first\nsecond\nthird\n";
        var modified = "first\ninserted\nsecond\nthird\n";
        var result = TextComparison.Compare(original, modified);
        Assert.Equal("inserted\n", Join(result, TextDifferenceKind.Added));
        Assert.Equal("", Join(result, TextDifferenceKind.Removed));
        AssertReconstructs(original, modified, result);
    }

    [Fact]
    public void RandomEditsAlwaysReconstructBothInputs()
    {
        var random = new Random(1234);
        const string alphabet = "abc \t\r\n";
        for (var sample = 0; sample < 300; sample++)
        {
            string Generate() => new(Enumerable.Range(0, random.Next(80)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var original = Generate();
            var modified = Generate();
            AssertReconstructs(original, modified, TextComparison.Compare(original, modified));
        }
    }

    [Fact]
    public void LargeChangesUseBoundedFallbackAndKeepSharedEnds()
    {
        var original = "prefix " + new string('x', 40_000) + " suffix";
        var modified = "prefix " + new string('y', 40_000) + " suffix";
        var result = TextComparison.Compare(original, modified);
        Assert.True(result.HasCoarseChanges);
        Assert.Equal(new string('x', 40_000), Join(result, TextDifferenceKind.Removed));
        AssertReconstructs(original, modified, result);
    }

    [Fact]
    public void ComparesThousandsOfLinesBeyondTheOldCharacterLimitWithoutTruncating()
    {
        var original = string.Concat(Enumerable.Range(1, 6000).Select(i => $"Line {i:D4}: {new string('x', 80)}\r\n"));
        var modified = original.Replace("Line 5500:", "Line 5501:");
        Assert.True(original.Length > 100_000);
        var result = TextComparison.Compare(original, modified);
        Assert.False(result.AreEqual);
        Assert.False(result.HasCoarseChanges);
        Assert.Equal("0", Join(result, TextDifferenceKind.Removed));
        Assert.Equal("1", Join(result, TextDifferenceKind.Added));
        AssertReconstructs(original, modified, result);
        Assert.True(TextComparison.Compare(original, original).AreEqual);
    }

    [Fact]
    public void LargeReplacementsFallBackWithoutDroppingText()
    {
        var original = "shared start\n" + new string('x', 1_000_000) + "\nshared end";
        var modified = "shared start\n" + new string('y', 1_000_000) + "\nshared end";
        var result = TextComparison.Compare(original, modified);
        Assert.True(result.HasCoarseChanges);
        AssertReconstructs(original, modified, result);
        Assert.Contains(result.Differences, change => change.Kind == TextDifferenceKind.Equal && change.Text.Contains("shared end"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LargeOneSidedTextIsRetainedInFull(bool added)
    {
        var text = new string('x', 1_000_000);
        var original = added ? "" : text;
        var modified = added ? text : "";
        var result = TextComparison.Compare(original, modified);
        AssertReconstructs(original, modified, result);
        Assert.False(result.HasCoarseChanges);
    }

    [Fact]
    public void HonorsCancellation()
    {
        Assert.Throws<OperationCanceledException>(() => TextComparison.Compare("a", "b", new CancellationToken(true)));
    }

    private static string Join(TextComparisonResult result, TextDifferenceKind kind) => string.Concat(result.Differences.Where(d => d.Kind == kind).Select(d => d.Text));

    private static void AssertReconstructs(string original, string modified, TextComparisonResult result)
    {
        Assert.Equal(original, string.Concat(result.Differences.Where(d => d.Kind != TextDifferenceKind.Added).Select(d => d.Text)));
        Assert.Equal(modified, string.Concat(result.Differences.Where(d => d.Kind != TextDifferenceKind.Removed).Select(d => d.Text)));
    }
}
