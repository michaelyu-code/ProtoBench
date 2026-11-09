using System.Text;
using NonStopToolkit.Generation.Definitions;
using NonStopToolkit.Generation.Engines;
using NonStopToolkit.Generation.Registry;
namespace NonStopToolkit.Generation.Tests;

public sealed class GeneratorTests
{
    private static GeneratorDefinition Definition() => GeneratorDefinition.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "definitions", "generators", "synthetic-fixed.json")));
    private static Dictionary<string, string> Inputs() => new() { ["kind"] = "FX", ["name"] = "ALPHA", ["count"] = "42" };
    [Fact]
    public async Task ExactBytesMatchFixture()
    {
        var g = new FixedWidthGenerator(Definition());
        using var output = new MemoryStream();
        var result = await g.GenerateAsync(Inputs(), output);
        Assert.True(result.Success);
        Assert.Equal(16, result.BytesWritten);
        Assert.Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "samples", "synthetic-generated.bin")), output.ToArray());
    }
    [Theory]
    [InlineData("name", "TOO LONG A NAME")]
    [InlineData("kind", "")]
    [InlineData("name", "café")]
    [InlineData("name", "a\nb")]
    [InlineData("extra", "x")]
    public async Task ValidatesBeforeWriting(string field, string value)
    {
        var values = Inputs();
        values[field] = value;
        using var output = new MemoryStream();
        output.WriteByte(123);
        var result = await new FixedWidthGenerator(Definition()).GenerateAsync(values, output);
        Assert.False(result.Success);
        Assert.Equal(new byte[] { 123 }, output.ToArray());
    }
    [Fact]
    public async Task WidthIsEncodedByteCount()
    {
        var d = Definition() with
        {
            Encoding = "utf-8",
            LineEnding = "none",
            Fields = [new() { Name = "x", Length = 4 }]
        };
        using var output = new MemoryStream();
        Assert.True((await new FixedWidthGenerator(d).GenerateAsync(new Dictionary<string, string> { { "x", "é" } }, output)).Success);
        Assert.Equal(new byte[] { 0xC3, 0xA9, 32, 32 }, output.ToArray());
        using var tooLong = new MemoryStream();
        Assert.False((await new FixedWidthGenerator(d).GenerateAsync(new Dictionary<string, string> { { "x", "ééé" } }, tooLong)).Success);
        Assert.Empty(tooLong.ToArray());
    }
    [Fact]
    public void RejectsBadDefinitions()
    {
        var d = Definition();
        Assert.Throws<ArgumentException>(() => (d with { SchemaVersion = 2 }).Validate());
        Assert.Throws<ArgumentException>(() => (d with { Fields = [new() { Name = "x", Length = 2, Padding = "é" }], Encoding = "utf-8" }).Validate());
        Assert.Throws<ArgumentException>(() => (d with { Fields = [new() { Name = "x", Length = 0 }] }).Validate());
        Assert.Throws<ArgumentException>(() => (d with { LineEnding = "unknown" }).Validate());
    }
    [Fact]
    public void DuplicateIdsRejected()
    {
        var g = new FixedWidthGenerator(Definition());
        Assert.Throws<ArgumentException>(() => new GeneratorRegistry([g, g]));
        Assert.Same(g, new GeneratorRegistry([g]).Get(g.GeneratorId));
    }
    [Fact]
    public async Task CancellationWritesNothing()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var output = new MemoryStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FixedWidthGenerator(Definition()).GenerateAsync(Inputs(), output, cts.Token));
        Assert.Empty(output.ToArray());
    }
}
