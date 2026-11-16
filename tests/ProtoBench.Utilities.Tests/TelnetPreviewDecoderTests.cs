using ProtoBench.Utilities.Networking;

namespace ProtoBench.Utilities.Tests;

public sealed class TelnetPreviewDecoderTests
{
    [Fact]
    public void SeparatesNegotiationAndUnescapesLiteralIac()
    {
        var result = new TelnetPreviewDecoder().Decode([255, 251, 1, 72, 105, 255, 253, 3, 255, 255, 13, 10]);
        Assert.Equal(new byte[] { 72, 105, 255, 13, 10 }, result.Data);
        Assert.Equal("IAC WILL ECHO (1)\nIAC DO SUPPRESS-GO-AHEAD (3)", result.Commands);
    }

    [Fact]
    public void DecodesSubnegotiationAndPreservesEscapedPayloadBytes()
    {
        var result = new TelnetPreviewDecoder().Decode([255, 250, 24, 0, 86, 84, 49, 48, 48, 255, 255, 255, 240, 65]);
        Assert.Equal(new byte[] { 65 }, result.Data);
        Assert.Equal("IAC SB TERMINAL-TYPE (24) · 7 payload bytes: 005654313030FF · IAC SE", result.Commands);
    }

    [Fact]
    public void EveryPossibleChunkBoundaryPreservesPayloadAndCommandState()
    {
        byte[] bytes = [65, 255, 251, 1, 255, 250, 31, 0, 80, 0, 24, 255, 255, 255, 240, 255, 255, 66];
        for (var split = 0; split <= bytes.Length; split++)
        {
            var decoder = new TelnetPreviewDecoder();
            var first = decoder.Decode(bytes.AsSpan(0, split));
            var second = decoder.Decode(bytes.AsSpan(split));
            Assert.Equal(new byte[] { 65, 255, 66 }, first.Data.Concat(second.Data).ToArray());
            var commands = first.Commands + second.Commands;
            Assert.Contains("IAC WILL ECHO (1)", commands);
            Assert.Contains("IAC SB NAWS (31) · 5 payload bytes: 00500018FF · IAC SE", commands);
            Assert.DoesNotContain("Incomplete", second.Commands);
        }
    }

    [Fact]
    public void ReplayMaintainsStateWithoutReturningPreviousOutput()
    {
        var decoder = new TelnetPreviewDecoder();
        var replay = decoder.Decode([65, 255, 250, 24, 0, 86], includeOutput: false);
        Assert.Empty(replay.Data); Assert.Empty(replay.Commands);
        var current = decoder.Decode([84, 255, 240, 66]);
        Assert.Equal(new byte[] { 66 }, current.Data);
        Assert.Contains("005654", current.Commands);
    }

    [Fact]
    public void ReportsPendingAndUnknownCommandsAndRecoversMalformedSubnegotiation()
    {
        var decoder = new TelnetPreviewDecoder();
        Assert.Contains("Incomplete", decoder.Decode([255]).Commands);
        Assert.Contains("Unknown option (200)", decoder.Decode([254, 200]).Commands);
        var result = decoder.Decode([255, 250, 24, 1, 255, 241, 65, 255, 200]);
        Assert.Equal(new byte[] { 65 }, result.Data);
        Assert.Contains("Malformed SB", result.Commands);
        Assert.Contains("IAC NOP", result.Commands);
        Assert.Contains("IAC command 0xC8", result.Commands);
    }

    [Fact]
    public void BoundsSubnegotiationAndCommandDisplay()
    {
        var decoder = new TelnetPreviewDecoder();
        decoder.Decode([255, 250, 24]);
        decoder.Decode(new byte[10000], includeOutput: false);
        var result = decoder.Decode([255, 240]);
        Assert.Contains("10000 payload bytes", result.Commands);
        Assert.Contains("limited to 64 bytes", result.Commands);
        Assert.True(result.Commands.Length < 300);
        var repeated = Enumerable.Range(0, 1000).SelectMany(_ => new byte[] { 255, 241 }).ToArray();
        result = decoder.Decode(repeated);
        Assert.Contains("744 additional commands omitted", result.Commands);
        Assert.Empty(result.Data);
    }
}
