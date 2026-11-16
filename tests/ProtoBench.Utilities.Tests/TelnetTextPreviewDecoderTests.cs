using System.Text;
using ProtoBench.Utilities.Networking;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Utilities.Tests;

public sealed class TelnetTextPreviewDecoderTests
{
    [Theory]
    [InlineData("\u001B[2J\u001B[H\u001B[32mLogin:\u001B[0m ", "Login: ")]
    [InlineData("\u001B]0;Window title\aHello", "Hello")]
    [InlineData("\u001B]8;;https://example.com\u001B\\Link\u001B]8;;\u001B\\", "Link")]
    [InlineData("A\u001BPprivate data\u001B\\B\u001B_hidden data\u001B\\C", "ABC")]
    [InlineData("\u001B(BHello\u001B[?25l\u001BOA", "Hello")]
    [InlineData("\0\aLogin:\tuser\r\0\nPassword:\u007F", "Login:\tuser\r\nPassword:")]
    [InlineData("\u001B[123\u0018Hello", "Hello")]
    public void StripsTerminalSequencesAndControls(string input, string expected)
    {
        var preview = new TelnetTextPreviewDecoder(Encoding.UTF8);
        Assert.Equal(expected, preview.Decode(Encoding.UTF8.GetBytes(input)).Text);
    }

    [Fact]
    public void EveryByteBoundaryPreservesTextAcrossTelnetAnsiAndUtf8Sequences()
    {
        byte[] input = [255, 251, 1, .. Encoding.UTF8.GetBytes("\u001B]0;Title\u001B\\\u001B[31mcafé 😀\u001B[0m\r\0\n")];
        for (var split = 0; split <= input.Length; split++)
        {
            var preview = new TelnetTextPreviewDecoder(Encoding.UTF8);
            var first = preview.Decode(input.AsSpan(0, split));
            var second = preview.Decode(input.AsSpan(split));
            Assert.Equal("café 😀\r\n", first.Text + second.Text);
            Assert.Contains("IAC WILL ECHO (1)", first.Commands + second.Commands);
        }
    }

    [Theory]
    [InlineData(28591)]
    [InlineData(1252)]
    [InlineData(437)]
    [InlineData(65001)]
    [InlineData(1200)]
    [InlineData(1201)]
    public void FiltersCharactersAfterDecodingSelectedEncoding(int codePage)
    {
        var encoding = TextEncodings.Options.Single(e => e.CodePage == codePage).GetEncoding(false);
        var preview = new TelnetTextPreviewDecoder(encoding);
        Assert.Equal("café\tOK\r\n", preview.Decode(encoding.GetBytes("\u001B[32mcafé\u001B[0m\tOK\r\0\n")).Text);
    }

    [Fact]
    public void ReplaySuppressesPriorTextAndRemembersEscapeAndCharacterState()
    {
        var preview = new TelnetTextPreviewDecoder(Encoding.UTF8);
        var prior = preview.Decode(Encoding.UTF8.GetBytes("Earlier\u001B[3"), includeOutput: false);
        Assert.Empty(prior.Text); Assert.Empty(prior.Commands);
        Assert.Equal("Hello", preview.Decode(Encoding.UTF8.GetBytes("1mHello\u001B[0m")).Text);
        preview.Decode([0xC3], includeOutput: false);
        Assert.Equal("é", preview.Decode([0xA9]).Text);
    }

    [Fact]
    public void UnterminatedControlStringUsesBoundedStateAndEndsOnTerminator()
    {
        var preview = new TelnetTextPreviewDecoder(Encoding.UTF8);
        preview.Decode(Encoding.UTF8.GetBytes("\u001B]0;"), includeOutput: false);
        Assert.Empty(preview.Decode(Encoding.UTF8.GetBytes(new string('x', 100000))).Text);
        Assert.Equal("Hello", preview.Decode(Encoding.UTF8.GetBytes("\aHello")).Text);
    }
}
