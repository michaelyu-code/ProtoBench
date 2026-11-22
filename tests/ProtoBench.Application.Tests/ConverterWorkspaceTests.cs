using ProtoBench.Desktop.Features.Converters;
using ProtoBench.Utilities.Numeric;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Application.Tests;

public sealed class ConverterWorkspaceTests
{
    [Theory]
    [InlineData(ConversionMode.AsciiToHex, "A B", "41 20 42")]
    [InlineData(ConversionMode.TextToCp500, "A B", "C1 40 C2")]
    [InlineData(ConversionMode.AsciiHexToCp500, "41 20 42", "C1 40 C2")]
    [InlineData(ConversionMode.Cp500ToAsciiHex, "C1 40 C2", "41 20 42")]
    public async Task CompactHexReformatsExistingResultsWithoutChangingTextData(ConversionMode mode, string input, string spaced)
    {
        using var workspace = new ConverterWorkspace(new BaseConverter(), new AsciiHexConverter(), new Cp500Converter())
        { Mode = mode, Input = input };
        await workspace.ConvertCommand.ExecuteAsync(null);
        Assert.True(workspace.ProducesHex);
        Assert.Equal(spaced, workspace.Output);
        workspace.CompactHexOutput = true;
        Assert.Equal(spaced.Replace(" ", ""), workspace.Output);
        workspace.CompactHexOutput = false;
        Assert.Equal(spaced, workspace.Output);
        workspace.CompactHexOutput = true;
        workspace.Input = "41 20 42";
        workspace.Mode = ConversionMode.HexToAscii;
        await workspace.ConvertCommand.ExecuteAsync(null);
        Assert.False(workspace.ProducesHex);
        Assert.Equal("A B", workspace.Output);
    }

    [Fact]
    public async Task SelectedEncodingAppliesToBothDirectionsAndCp500Transcoding()
    {
        using var workspace = new ConverterWorkspace(new BaseConverter(), new AsciiHexConverter(), new Cp500Converter())
        {
            Mode = ConversionMode.AsciiToHex, Input = "café"
        };
        await workspace.ConvertCommand.ExecuteAsync(null);
        Assert.NotEmpty(workspace.Error);
        workspace.TextEncoding = TextEncodings.Options.Single(e => e.CodePage == 28591);
        Assert.Empty(workspace.Error);
        await workspace.ConvertCommand.ExecuteAsync(null);
        Assert.Equal("63 61 66 E9", workspace.Output);
        workspace.TextEncoding = TextEncodings.Utf8;
        Assert.Empty(workspace.Output);
        await workspace.ConvertCommand.ExecuteAsync(null);
        Assert.Equal("63 61 66 C3 A9", workspace.Output);
        workspace.Input = workspace.Output;
        workspace.Mode = ConversionMode.HexToAscii;
        await workspace.ConvertCommand.ExecuteAsync(null);
        Assert.Equal("café", workspace.Output);
        workspace.Mode = ConversionMode.AsciiHexToCp500;
        await workspace.ConvertCommand.ExecuteAsync(null);
        var cp500Hex = workspace.Output;
        Assert.Equal(new Cp500Converter().ToHex("café"), cp500Hex);
        workspace.Input = cp500Hex;
        workspace.Mode = ConversionMode.Cp500ToAsciiHex;
        await workspace.ConvertCommand.ExecuteAsync(null);
        Assert.Equal("63 61 66 C3 A9", workspace.Output);
    }
}
