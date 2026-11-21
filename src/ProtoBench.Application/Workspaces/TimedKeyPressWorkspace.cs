using System.Globalization;
using ProtoBench.Application.Services;

namespace ProtoBench.Application.Workspaces;

/// <summary>UI-thread session; Tick is driven independently of whether its tab is visible.</summary>
public sealed class TimedKeyPressWorkspace(IKeyboardInput keyboard, TimeProvider? timeProvider = null) : ToolWorkspace
{
    public override string ToolId => "timed-key-presser";
    public override string Title => "Timed Key Presser";
    public const int MaximumLogEntries = 1000;
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Queue<string> entries = new();
    private KeyboardKey selectedKey = KeyboardKeys.All.Single(k => k.Name == "F24");
    private string intervalSeconds = "300", activity = "", error = "";
    private TimeSpan interval;
    private long lastAttempt;
    private bool disposed;
    private DateTimeOffset? nextPress;
    private long pressCount;
    public IReadOnlyList<KeyboardKey> Keys => KeyboardKeys.All;
    public KeyboardKey SelectedKey
    {
        get => selectedKey;
        set { if (CanConfigure && SetProperty(ref selectedKey, value)) Error = ""; }
    }
    public string IntervalSeconds
    {
        get => intervalSeconds;
        set { if (CanConfigure && SetProperty(ref intervalSeconds, value)) Error = ""; }
    }
    public bool IsRunning => IsBusy;
    public bool CanConfigure => !IsRunning && !disposed;
    public string ToggleLabel => IsRunning ? "Stop" : "Start";
    public string Activity { get => activity; private set => SetProperty(ref activity, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public long PressCount { get => pressCount; private set => SetProperty(ref pressCount, value); }
    public string Summary => $"{PressCount:N0} presses sent" + (nextPress is { } next ? $" · next due {next:HH:mm:ss zzz}" : "")
        + $" · latest {MaximumLogEntries:N0} log entries retained";

    public void Toggle() { if (IsRunning) Stop(); else Start(); }

    public void Start()
    {
        if (!CanConfigure) return;
        Error = "";
        if (SelectedKey is null || !Keys.Contains(SelectedKey)) { Error = "Select a supported keyboard key."; return; }
        if (!double.TryParse(IntervalSeconds, NumberStyles.Float, CultureInfo.CurrentCulture, out var seconds)
            || !double.IsFinite(seconds) || seconds < 1 || seconds > 86400)
        { Error = "Enter an interval from 1 to 86,400 seconds (300 = 5 minutes)."; return; }
        interval = TimeSpan.FromSeconds(seconds);
        lastAttempt = clock.GetTimestamp();
        nextPress = clock.GetLocalNow() + interval;
        Status = $"Running · {SelectedKey.Name} every {seconds:G} seconds";
        IsBusy = true;
        Append($"Started {SelectedKey.Name} every {seconds:G} seconds. First press after one interval.");
        NotifyState();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsBusy = false;
        nextPress = null;
        Status = "Stopped";
        Append("Stopped. No more key presses will be sent.");
        NotifyState();
    }

    public void Tick()
    {
        if (!IsRunning || disposed || clock.GetElapsedTime(lastAttempt) < interval) return;
        try
        {
            if (keyboard.Press(SelectedKey) == KeyPressResult.Sent)
            {
                PressCount++;
                Append($"Pressed {SelectedKey.Name} · #{PressCount:N0}");
            }
            else Append($"Skipped {SelectedKey.Name}: a modifier or the selected key is held down.");
            // Schedule from now, not from the old deadline: waking/resuming never
            // produces a burst of overdue key presses.
            lastAttempt = clock.GetTimestamp();
            nextPress = clock.GetLocalNow() + interval;
            OnPropertyChanged(nameof(Summary));
        }
        catch (Exception ex)
        {
            Stop();
            Error = ex.Message;
            Status = "Stopped after a key press failed";
            Append($"Key press failed: {ex.Message}");
        }
    }

    private void Append(string message)
    {
        entries.Enqueue($"{clock.GetLocalNow():yyyy-MM-dd HH:mm:ss.fff zzz}  {message}");
        while (entries.Count > MaximumLogEntries) entries.Dequeue();
        Activity = string.Join(Environment.NewLine, entries);
    }

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(CanConfigure));
        OnPropertyChanged(nameof(ToggleLabel)); OnPropertyChanged(nameof(Summary));
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop(); base.Dispose();
        entries.Clear(); Activity = "";
        NotifyState();
    }
}
