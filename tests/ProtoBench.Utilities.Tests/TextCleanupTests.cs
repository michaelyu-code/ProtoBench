using ProtoBench.Utilities.Text;

namespace ProtoBench.Utilities.Tests;

public sealed class TextCleanupTests
{
    [Theory]
    [InlineData("A B\tC\r\nD", "AB\tC\r\nD", 1)]
    [InlineData("A\u00A0B", "A\u00A0B", 0)]
    public void SpacesOnlyPreservesOtherWhitespace(string input, string expected, int removed)
    {
        var result = TextCleanup.Clean(input, new() { Spaces = true });
        Assert.Equal(expected, result.Text);
        Assert.Equal(removed, result.RemovedCharacters);
    }

    [Fact]
    public void LineEndingsIncludesMixedWindowsAndUnicodeSeparators()
    {
        Assert.Equal("ABCDEFG", TextCleanup.Clean("A\r\nB\rC\nD\u0085E\u2028F\u2029G", new() { LineEndings = true }).Text);
        Assert.Equal("ABCD", TextCleanup.Clean("A \tB\r\nC\u00A0D", new() { AllWhitespace = true }).Text);
        Assert.Equal("A B\nC", TextCleanup.Clean("A B\t\nC", new() { Tabs = true }).Text);
    }

    [Fact]
    public void ControlsDoNotRemoveTabsOrLineEndingsUnlessSelected()
    {
        Assert.Equal("A\tB\r\nC", TextCleanup.Clean("\0A\tB\r\nC\u007F", new() { ControlCharacters = true }).Text);
    }

    [Fact]
    public void UnicodeAndCustomCharactersAreRemovedLiterallyWithoutDamagingEmoji()
    {
        var result = TextCleanup.Clean("A1٢,。🙂🙃[]*B", new() { Digits = true, Punctuation = true, CustomCharacters = "🙂" });
        Assert.Equal("A🙃B", result.Text);
        Assert.Equal(8, result.RemovedCharacters);
        Assert.Equal("abc", TextCleanup.Clean("a.b*c[", new() { CustomCharacters = ".*[" }).Text);
        Assert.Equal("🙂🙃", TextCleanup.Clean("🙂🙃", new()).Text);
    }

    [Fact]
    public void CleanupHonorsCancellationAndInputLimit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => TextCleanup.Clean("hello", new(), cancellation.Token));
        Assert.Throws<FormatException>(() => TextCleanup.Clean(new string('x', TextCleanup.MaxCharacters + 1), new()));
        Assert.Equal("", TextCleanup.Clean("", new()).Text);
    }
}
