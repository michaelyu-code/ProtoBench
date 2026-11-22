using ProtoBench.Desktop.Features.Calculators;

namespace ProtoBench.Application.Tests;

public sealed class CalculatorWorkspaceTests
{
    [Theory]
    [InlineData(1, "ABCDEFGH", "4142434445464748")]
    [InlineData(2, "Aé🙂", "41C3A9F09F9982")]
    [InlineData(1, "        ", "2020202020202020")]
    public async Task TextFormatsEncryptTheExactEncodedBytes(int format, string text, string hex)
    {
        const string key = "0123456789ABCDEFFEDCBA9876543210";
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes)
        { Key = key, Input = text, InputFormat = format, Padding = 1, Verbose = true };
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Equal(ProtoBench.Utilities.Binary.TripleDesCalculator.Transform(hex, key, "", false,
            System.Security.Cryptography.CipherMode.ECB, System.Security.Cryptography.PaddingMode.PKCS7), workspace.Output);
        Assert.Contains($"Input bytes: {hex}", workspace.CalculationDetails);
        Assert.Equal(text, workspace.Input);
        Assert.Empty(workspace.Error);
    }

    [Theory]
    [InlineData(0, "0 0 00\t0000\r\n00000000", "0000000000000000")]
    [InlineData(1, "AB CD\tEF\r\nGH", "4142434445464748")]
    [InlineData(2, "A é\t🙂", "41C3A9F09F9982")]
    public async Task PackingRemovesWhitespaceBeforeInterpretingInput(int format, string input, string hex)
    {
        const string key = "0123456789ABCDEFFEDCBA9876543210";
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes)
        { Key = key, Input = input, InputFormat = format, PackInput = true, Padding = 1, Verbose = true };
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Equal(ProtoBench.Utilities.Binary.TripleDesCalculator.Transform(hex, key, "", false,
            System.Security.Cryptography.CipherMode.ECB, System.Security.Cryptography.PaddingMode.PKCS7), workspace.Output);
        Assert.Equal(input, workspace.Input);
        Assert.Contains("remove whitespace): on", workspace.CalculationDetails);
        workspace.PackInput = false;
        Assert.Empty(workspace.Output);
        Assert.Empty(workspace.CalculationDetails);
    }

    [Fact]
    public async Task InvalidTextEncodingAndByteLimitsFlagTheInput()
    {
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes)
        { Key = "0123456789ABCDEFFEDCBA9876543210", InputFormat = 1, Input = "é", Padding = 1 };
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("Select UTF-8", workspace.InputError);
        Assert.Empty(workspace.Output);
        workspace.InputFormat = 2;
        workspace.Input = new string('é', 9000);
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("16,384 bytes", workspace.InputError);
        workspace.Input = "\uD800";
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("invalid character", workspace.InputError);
        workspace.Input = " \t\n";
        workspace.PackInput = true;
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("Enter plaintext", workspace.InputError);
    }

    [Fact]
    public async Task DefaultHexAndHexDecryptionRemainCompatible()
    {
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes)
        { Key = "0123456789ABCDEFFEDCBA9876543210", Input = "08 D7 B4 FB 62 9D 08 85", Mode = 1 };
        Assert.Equal(0, workspace.InputFormat);
        Assert.False(workspace.PackInput);
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Equal("0000000000000000", workspace.Output);
        workspace.Input = "0 8D7B4FB629D0885";
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("adjacent digits", workspace.InputError);
        workspace.PackInput = true;
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Equal("0000000000000000", workspace.Output);
    }

    [Fact]
    public async Task CalculateHighlightsAllMissingRequiredFieldsAndRecoversAfterEditing()
    {
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes) { Cipher = 1 };
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.NotEmpty(workspace.KeyError);
        Assert.NotEmpty(workspace.InputError);
        Assert.NotEmpty(workspace.IvError);
        Assert.Contains("required", workspace.Error);
        Assert.Equal("Check inputs", workspace.Status);
        Assert.Empty(workspace.Output);
        workspace.Key = "0123456789ABCDEFFEDCBA9876543210";
        workspace.Input = "0000000000000000";
        workspace.Iv = "0000000000000000";
        Assert.Empty(workspace.KeyError);
        Assert.Empty(workspace.InputError);
        Assert.Empty(workspace.IvError);
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Equal("08D7B4FB629D0885", workspace.Output);
        Assert.Empty(workspace.Error);
    }

    [Fact]
    public async Task EcbDoesNotRequireIvAndKcvRequiresOnlyTheKey()
    {
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes) { Mode = 1, Input = " " };
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("ciphertext", workspace.InputError);
        Assert.Empty(workspace.IvError);
        workspace.CalculateKcvCommand.Execute(null);
        Assert.NotEmpty(workspace.KeyError);
        workspace.Key = "0123456789ABCDEFFEDCBA9876543210";
        workspace.Cipher = 1;
        workspace.CalculateKcvCommand.Execute(null);
        Assert.Equal("08D7B4", workspace.Kcv);
        Assert.Empty(workspace.Error);
        Assert.Empty(workspace.InputError);
        Assert.Empty(workspace.IvError);
        workspace.ClearCommand.Execute(null);
        Assert.Empty(workspace.KeyError);
    }

    [Fact]
    public async Task VerboseResultsKeepTheCopyableCiphertextSeparateAndClearExposedKeys()
    {
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes)
        {
            Key = "0123456789ABCDEFFEDCBA9876543210", Input = "0000000000000000",
            Verbose = true, IncludeKeyValues = true
        };
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Equal("08D7B4FB629D0885", workspace.Output);
        Assert.Contains("K1: 0123456789ABCDEF", workspace.DisplayOutput);
        Assert.DoesNotContain("Calculation details", workspace.Output);
        workspace.ShowKey = false;
        Assert.NotEmpty(workspace.Output);
        workspace.IncludeKeyValues = false;
        Assert.Empty(workspace.CalculationDetails);
        Assert.Empty(workspace.DisplayOutput);
        await workspace.CalculateCommand.ExecuteAsync(null);
        Assert.Contains("K1: [hidden]", workspace.CalculationDetails);
        workspace.ClearCommand.Execute(null);
        Assert.Empty(workspace.Key);
        Assert.Empty(workspace.CalculationDetails);
    }

    [Fact]
    public void VerboseKcvExplainsZeroBlockEncryptionIndependentOfSelectedMode()
    {
        using var workspace = new CalculatorWorkspace(CalculatorKind.TripleDes)
        {
            Key = "0123456789ABCDEFFEDCBA9876543210", Cipher = 1, Mode = 1, Verbose = true
        };
        workspace.CalculateKcvCommand.Execute(null);
        Assert.Equal("08D7B4", workspace.Kcv);
        Assert.Contains("KCV = first 3 bytes", workspace.CalculationDetails);
        Assert.Contains("Encrypt · ECB · None", workspace.CalculationDetails);
        Assert.Contains("0000000000000000", workspace.CalculationDetails);
        Assert.DoesNotContain("0123456789ABCDEF", workspace.CalculationDetails);
    }
}
