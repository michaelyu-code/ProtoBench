using ProtoBench.Application.Services;
using ProtoBench.Application.Workspaces;

namespace ProtoBench.Application.Tests;

public sealed class TimedKeyPressWorkspaceTests
{
    private sealed class Clock : TimeProvider
    {
        private long timestamp;
        private DateTimeOffset now = new(2026, 11, 22, 0, 0, 0, TimeSpan.Zero);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => timestamp;
        public override DateTimeOffset GetUtcNow() => now;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public void Advance(double seconds) { timestamp += TimeSpan.FromSeconds(seconds).Ticks; now += TimeSpan.FromSeconds(seconds); }
        public void MoveWallClock(double seconds) => now += TimeSpan.FromSeconds(seconds);
    }
    private sealed class Keyboard : IKeyboardInput
    {
        public List<KeyboardKey> Pressed { get; } = [];
        public KeyPressResult Result { get; set; } = KeyPressResult.Sent;
        public bool Fail { get; set; }
        public KeyPressResult Press(KeyboardKey key)
        {
            if (Fail) throw new InvalidOperationException("Input blocked");
            if (Result == KeyPressResult.Sent) Pressed.Add(key);
            return Result;
        }
    }

    [Fact]
    public void FirstPressWaitsForIntervalAndEachPressIsTimestamped()
    {
        var clock = new Clock(); var keyboard = new Keyboard();
        using var vm = new TimedKeyPressWorkspace(keyboard, clock) { IntervalSeconds = "5" };
        vm.Start(); vm.Tick(); clock.Advance(4); vm.Tick();
        Assert.Empty(keyboard.Pressed);
        clock.Advance(1); vm.Tick(); vm.Tick();
        Assert.Single(keyboard.Pressed);
        Assert.Equal("F24", keyboard.Pressed[0].Name);
        Assert.Contains("2026-11-22 00:00:05.000 +00:00  Pressed F24", vm.Activity);
        clock.Advance(5); vm.Tick();
        Assert.Equal(2, vm.PressCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("86401")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("five minutes")]
    public void InvalidIntervalsDoNotStart(string interval)
    {
        using var vm = new TimedKeyPressWorkspace(new Keyboard(), new Clock()) { IntervalSeconds = interval };
        vm.Start();
        Assert.False(vm.IsRunning);
        Assert.True(vm.CanConfigure);
        Assert.NotEmpty(vm.Error);
        Assert.Empty(vm.Activity);
    }

    [Fact]
    public void RunningInputsAreLockedAndStopPreventsFurtherPresses()
    {
        var clock = new Clock(); var keyboard = new Keyboard();
        using var vm = new TimedKeyPressWorkspace(keyboard, clock) { IntervalSeconds = "5" };
        vm.Toggle();
        vm.SelectedKey = KeyboardKeys.All.Single(k => k.Name == "A"); vm.IntervalSeconds = "1";
        Assert.Equal("F24", vm.SelectedKey.Name); Assert.Equal("5", vm.IntervalSeconds);
        Assert.False(vm.CanConfigure); Assert.Equal("Stop", vm.ToggleLabel);
        vm.Toggle(); clock.Advance(20); vm.Tick();
        Assert.Empty(keyboard.Pressed); Assert.True(vm.CanConfigure); Assert.Equal("Start", vm.ToggleLabel);
        vm.SelectedKey = KeyboardKeys.All.Single(k => k.Name == "A"); vm.IntervalSeconds = "1";
        vm.Start(); clock.Advance(1); vm.Tick();
        Assert.Equal("A", Assert.Single(keyboard.Pressed).Name);
    }

    [Fact]
    public void LateTicksDoNotReplayMissedPressesAndWallClockChangesDoNotTriggerPresses()
    {
        var clock = new Clock(); var keyboard = new Keyboard();
        using var vm = new TimedKeyPressWorkspace(keyboard, clock) { IntervalSeconds = "5" };
        vm.Start(); clock.MoveWallClock(3600); vm.Tick();
        Assert.Empty(keyboard.Pressed);
        clock.Advance(300); vm.Tick(); vm.Tick();
        Assert.Single(keyboard.Pressed);
        clock.Advance(4); vm.Tick(); Assert.Single(keyboard.Pressed);
        clock.Advance(1); vm.Tick(); Assert.Equal(2, keyboard.Pressed.Count);
    }

    [Fact]
    public void HeldKeysAreLoggedAsSkippedRatherThanSuccessfulPresses()
    {
        var clock = new Clock(); var keyboard = new Keyboard { Result = KeyPressResult.HeldKeySkipped };
        using var vm = new TimedKeyPressWorkspace(keyboard, clock) { IntervalSeconds = "1" };
        vm.Start(); clock.Advance(1); vm.Tick();
        Assert.Equal(0, vm.PressCount); Assert.Contains("Skipped F24", vm.Activity); Assert.True(vm.IsRunning);
        keyboard.Result = KeyPressResult.Sent; clock.Advance(1); vm.Tick();
        Assert.Equal(1, vm.PressCount);
    }

    [Fact]
    public void FailedInjectionStopsTheSessionAndRecordsTheFailure()
    {
        var clock = new Clock(); var keyboard = new Keyboard { Fail = true };
        using var vm = new TimedKeyPressWorkspace(keyboard, clock) { IntervalSeconds = "1" };
        vm.Start(); clock.Advance(1); vm.Tick();
        Assert.False(vm.IsRunning); Assert.Equal(0, vm.PressCount);
        Assert.Equal("Input blocked", vm.Error); Assert.Contains("Key press failed: Input blocked", vm.Activity);
        keyboard.Fail = false; clock.Advance(1); vm.Tick(); Assert.Empty(keyboard.Pressed);
    }

    [Fact]
    public void DisposalStopsAndCannotBeRestartedAndActivityIsBounded()
    {
        var clock = new Clock(); var keyboard = new Keyboard();
        var vm = new TimedKeyPressWorkspace(keyboard, clock) { IntervalSeconds = "1" };
        vm.Start();
        for (var i = 0; i < 1100; i++) { clock.Advance(1); vm.Tick(); }
        Assert.Equal(1100, vm.PressCount);
        Assert.Equal(TimedKeyPressWorkspace.MaximumLogEntries, vm.Activity.Split(Environment.NewLine).Length);
        Assert.DoesNotContain("Started", vm.Activity);
        Assert.Contains("#1,100", vm.Activity);
        vm.Dispose(); vm.Start(); clock.Advance(1); vm.Tick();
        Assert.False(vm.IsRunning); Assert.False(vm.CanConfigure); Assert.Empty(vm.Activity);
        Assert.Equal(1100, keyboard.Pressed.Count);
    }

    [Fact]
    public void KeyCatalogContainsWindowsFunctionKeysAndRejectsInventedKeys()
    {
        Assert.Equal(24, KeyboardKeys.All.Count(k => k.Name.StartsWith('F') && int.TryParse(k.Name[1..], out _)));
        Assert.Equal(0x87, KeyboardKeys.All.Single(k => k.Name == "F24").VirtualKey);
        Assert.DoesNotContain(KeyboardKeys.All, k => k.Name == "F25");
        using var vm = new TimedKeyPressWorkspace(new Keyboard()) { SelectedKey = new("F25", 0x88) };
        vm.Start(); Assert.False(vm.IsRunning); Assert.NotEmpty(vm.Error);
    }
}
