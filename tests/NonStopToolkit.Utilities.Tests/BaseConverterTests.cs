using NonStopToolkit.Utilities.Numeric;
namespace NonStopToolkit.Utilities.Tests;

public sealed class BaseConverterTests
{
    private readonly BaseConverter converter = new();
    [Theory]
    [InlineData("255", 10, 16, "FF")]
    [InlineData("FF", 16, 10, "255")]
    [InlineData("1010", 2, 10, "10")]
    [InlineData("10", 10, 2, "1010")]
    [InlineData("FF", 16, 2, "11111111")]
    [InlineData("11111111", 2, 16, "FF")]
    [InlineData("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", 16, 10, "340282366920938463463374607431768211455")]
    [InlineData("-255", 10, 16, "-FF")]
    [InlineData("-0", 10, 2, "0")]
    [InlineData("0b1010", 2, 16, "A")]
    [InlineData(" +ff ", 16, 10, "255")]
    public void Converts(string text, int from, int to, string expected) => Assert.Equal(expected, converter.Convert(text, from, to));
    [Theory]
    [InlineData("", 10)]
    [InlineData("0x", 16)]
    [InlineData("102", 2)]
    [InlineData("1 2", 10)]
    [InlineData("FG", 16)]
    [InlineData("-", 10)]
    public void RejectsInvalidInput(string text, int from) => Assert.Throws<FormatException>(() => converter.Convert(text, from, 10));
    [Fact] public void RejectsUnsupportedBase() => Assert.Throws<ArgumentException>(() => converter.Convert("1", 8, 10));
    [Fact] public void LimitsInput() => Assert.Throws<FormatException>(() => converter.Convert(new string('1', 16385), 2, 10));
    [Fact]
    public void RoundTripsLargeValue()
    {
        var value = new string('F', 512);
        Assert.Equal(value, converter.Convert(converter.Convert(value, 16, 10), 10, 16));
    }
}
