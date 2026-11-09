using NonStopToolkit.Desktop.Features.Parsers;

namespace NonStopToolkit.Application.Tests;

public sealed class ParserTablePanTests
{
    [Fact]
    public void HoldingMiddleButtonScrollsContinuouslyWithDeadZoneAndBoundedSpeed()
    {
        var pan = new ParserTablePan();
        pan.Begin(100, 100);
        Assert.True(pan.Active);
        Assert.True(pan.Held);
        pan.Move(108, 105); Assert.Equal((0d, 0d), pan.AutoStep);
        pan.Move(140, 70); Assert.Equal(3.6, pan.AutoStep.X, 6); Assert.Equal(-2.4, pan.AutoStep.Y, 6);
        // Repeated timer ticks keep scrolling even when the pointer stops moving.
        var step = pan.AutoStep;
        for (var tick = 0; tick < 60; tick++) Assert.Equal(step, pan.AutoStep);
        pan.Move(10000, -10000); Assert.Equal((32d, -32d), pan.AutoStep);
        pan.Stop(); Assert.False(pan.Active); Assert.Equal((0d, 0d), pan.AutoStep);
    }

    [Fact]
    public void ReleaseStopsImmediatelyAndLaterPointerMovementCannotRestartScrolling()
    {
        var pan = new ParserTablePan();
        pan.Begin(100, 100); pan.Move(60, 75);
        Assert.Equal(-3.6, pan.AutoStep.X, 6);
        Assert.Equal(-1.8, pan.AutoStep.Y, 6);
        pan.Release(); Assert.False(pan.Active);
        Assert.False(pan.Held);
        pan.Move(500, 500);
        Assert.Equal((0d, 0d), pan.AutoStep);
        pan.Begin(500, 500);
        Assert.Equal((0d, 0d), pan.AutoStep);
    }

    [Fact]
    public void ReturningToAnchorStopsMovementAndReversingDirectionReversesScroll()
    {
        var pan = new ParserTablePan();
        pan.Begin(200, 200);
        pan.Move(250, 200); Assert.True(pan.AutoStep.X > 0); Assert.Equal(0, pan.AutoStep.Y);
        pan.Move(200, 200); Assert.Equal((0d, 0d), pan.AutoStep); Assert.True(pan.Active);
        pan.Move(150, 250); Assert.True(pan.AutoStep.X < 0); Assert.True(pan.AutoStep.Y > 0);
        pan.Stop(); Assert.Equal((0d, 0d), pan.AutoStep);
    }
}
