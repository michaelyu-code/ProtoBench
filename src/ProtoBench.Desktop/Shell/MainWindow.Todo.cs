using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using ProtoBench.Application.Services;
using ProtoBench.Core.Documents;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Features.Todo;

namespace ProtoBench.Desktop.Shell;

public sealed partial class MainWindow
{
    private readonly TodoManager todos;
    private readonly DispatcherTimer todoClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly InfoBar todoNotice = new() { Severity = InfoBarSeverity.Warning, Title = "Time’s up", IsClosable = true };
    private readonly InfoBar todoSaveError = new() { Severity = InfoBarSeverity.Error, Title = "Tasks are not saved", IsClosable = false };
    private readonly InfoBar todoRunning = new() { Severity = InfoBarSeverity.Informational, Title = "Focusing", IsClosable = false };
    private Guid? noticeId;
    private bool notificationRegistered, refreshingTodo;
    private string desktopNotificationStatus = "";
    private readonly Dictionary<Guid, uint> desktopNotices = [];

    private void TodoAction(Action action)
    {
        try { action(); }
        catch (Exception ex) { Report(ex); }
    }
    private void SetupTodoNotifications()
    {
        // Put timer notices above the navigation so they remain visible in every workspace.
        root.RowDefinitions.Insert(3, new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(navigation, 4);
        // Closed InfoBars have zero height but still count toward StackPanel spacing.
        // Keep banners flush so this row takes no space when all notices are closed.
        var notices = new StackPanel();
        notices.Children.Add(todoNotice);
        notices.Children.Add(todoRunning);
        notices.Children.Add(todoSaveError);
        Grid.SetRow(notices, 3); root.Children.Add(notices);
        todoSaveError.ActionButton = Ui.Button("Retry saving", () => TodoAction(todos.RetrySave));
        todoRunning.Content = Ui.Row(
            Ui.Button("Open task", () => { if (todos.Items.FirstOrDefault(t => t.Deadline is not null) is { } task) OpenTodo(task.Id); }),
            Ui.Button("Pause", () => { if (todos.Items.FirstOrDefault(t => t.Deadline is not null) is { } task) TodoAction(() => todos.Pause(task.Id)); }));
        todoNotice.Content = Ui.Row(
            Ui.Primary(Ui.Button("+5 minutes", () => { if (noticeId is { } id) TodoAction(() => todos.Start(id, extend: true)); })),
            Ui.Button("Restart", () => { if (noticeId is { } id) TodoAction(() => todos.Start(id, restart: true)); }),
            Ui.Button("Mark complete", () => { if (noticeId is { } id) TodoAction(() => todos.Complete(id, true)); }),
            Ui.Button("Open task", () => { if (noticeId is { } id) OpenTodo(id); }));
        todoNotice.Closed += (_, _) => { if (!refreshingTodo && noticeId is { } id) TodoAction(() => todos.Acknowledge(id)); };
        todos.Changed += RefreshTodoNotice;
        todos.TimerEnded += ShowTodoDesktopNotice;
        todoClock.Tick += (_, _) => { TodoAction(todos.Tick); PaintTodoRunning(); };
        AppNotificationManager.Default.NotificationInvoked += TodoNotificationInvoked;
        try { AppNotificationManager.Default.Register(); notificationRegistered = true; }
        catch (Exception ex)
        {
            desktopNotificationStatus = "Windows desktop notifications are unavailable. In-app timer banners still work.";
            logger.LogWarning(new EventId(111), ex, "Task desktop notification registration failed");
        }
        Closed += (_, _) =>
        {
            todoClock.Stop(); todos.Changed -= RefreshTodoNotice; todos.TimerEnded -= ShowTodoDesktopNotice;
            AppNotificationManager.Default.NotificationInvoked -= TodoNotificationInvoked;
            if (notificationRegistered)
            {
                try { AppNotificationManager.Default.Unregister(); }
                catch (Exception ex) { logger.LogWarning(new EventId(112), ex, "Task notification shutdown failed"); }
            }
        };
    }
    private void RefreshTodoNotice()
    {
        if (windowClosed) return;
        refreshingTodo = true;
        try
        {
            var pending = todos.Items.FirstOrDefault(t => t.NeedsAttention && t.CompletedAt is null);
            noticeId = pending?.Id;
            todoNotice.Message = pending is null ? "" : $"{pending.Title} · Your timebox has ended. The task is still open.";
            todoNotice.IsOpen = pending is not null;
            todoSaveError.Message = todos.SaveError;
            todoSaveError.IsOpen = todos.SaveError.Length > 0;
            PaintTodoRunning();
            foreach (var tab in tabs.TabItems.OfType<TabViewItem>().Where(t => t.Tag is TodoWorkspace))
            {
                var title = tab.Header?.ToString()?.Replace(" ●", "") ?? "To-do";
                tab.Header = title + (pending is null ? "" : " ●");
                ToolTipService.SetToolTip(tab, pending is null ? title : title + " · Timer needs attention");
            }
            foreach (var id in desktopNotices.Keys.Where(id => !todos.Items.Any(t => t.Id == id && t.NeedsAttention)).ToArray())
            {
                var notificationId = desktopNotices[id]; desktopNotices.Remove(id);
                _ = RemoveDesktopNoticeAsync(notificationId);
            }
            if (pending is null) FlashTaskbar(false);
        }
        finally { refreshingTodo = false; }
    }
    private void PaintTodoRunning()
    {
        if (windowClosed) return;
        var task = todos.Items.FirstOrDefault(t => t.Deadline is not null);
        if (task is not null)
        {
            var seconds = (int)Math.Ceiling(todos.Remaining(task));
            todoRunning.Message = $"{task.Title} · {seconds / 60:00}:{seconds % 60:00} remaining";
        }
        todoRunning.IsOpen = task is not null;
    }
    private async Task RemoveDesktopNoticeAsync(uint id)
    {
        try { await AppNotificationManager.Default.RemoveByIdAsync(id); }
        catch (Exception ex) { logger.LogWarning(new EventId(113), ex, "Task desktop notification removal failed"); }
    }
    private void ShowTodoDesktopNotice(TodoItem task)
    {
        if (GetForegroundWindow() == WinRT.Interop.WindowNative.GetWindowHandle(this)) return;
        FlashTaskbar(true);
        if (!notificationRegistered) return;
        try
        {
            var notice = new AppNotificationBuilder().AddArgument("task", task.Id.ToString())
                .AddText("Time’s up · " + task.Title).AddText("Your timebox has ended. Open ProtoBench to extend it or complete the task.")
                .MuteAudio().BuildNotification();
            notice.Expiration = DateTimeOffset.Now.AddDays(1);
            AppNotificationManager.Default.Show(notice);
            desktopNotices[task.Id] = notice.Id;
        }
        catch (Exception ex)
        {
            desktopNotificationStatus = "Windows could not show the desktop notification. In-app timer banners remain available.";
            logger.LogWarning(new EventId(114), ex, "Task desktop notification failed");
        }
    }
    private void TodoNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        if (!args.Arguments.TryGetValue("task", out var value) || !Guid.TryParse(value, out var id)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (windowClosed) return;
            if (AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
            OpenTodo(id);
            Activate();
            SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        });
    }
    internal void HandleTodoLaunch()
    {
        if (!notificationRegistered) return;
        try
        {
            var activation = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs args)
                TodoNotificationInvoked(AppNotificationManager.Default, args);
        }
        catch (Exception ex) { logger.LogWarning(new EventId(115), ex, "Task notification launch handling failed"); }
    }
    private void OpenTodo(Guid id)
    {
        if (Open("todo") is TodoWorkspace workspace)
        {
            workspace.SelectedTask = todos.Items.Any(t => t.Id == id) ? id : null;
            // Recreate the selected view so a notification can select a task in an already open tab.
            var tab = tabs.TabItems.OfType<TabViewItem>().First(t => ReferenceEquals(t.Tag, workspace));
            tab.Content = views["todo"].Create(workspace);
        }
        RefreshTodoNotice();
    }
    private UIElement CreateTodoSettings()
    {
        var options = new ComboBox { Header = "When a task timer ends", HorizontalAlignment = HorizontalAlignment.Stretch };
        options.Items.Add(new ComboBoxItem { Content = "Visual only" });
        options.Items.Add(new ComboBoxItem { Content = "Sound + visual (coming later)", IsEnabled = false });
        options.Items.Add(new ComboBoxItem { Content = "Sound only (coming later)", IsEnabled = false });
        options.SelectedIndex = 0;
        var forever = new CheckBox { Content = "Keep completed tasks forever, until I remove them", IsChecked = todos.RetentionDays is null };
        var days = new NumberBox { Header = "Otherwise, remove this many days after completion", Minimum = 1, Maximum = 3650,
            Value = todos.RetentionDays ?? 14, IsEnabled = todos.RetentionDays is not null, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var impact = Ui.Text(""); var result = Ui.Text("");
        void Preview()
        {
            days.IsEnabled = forever.IsChecked != true;
            impact.Text = forever.IsChecked == true ? "Completed tasks remain until you remove them." :
                double.IsFinite(days.Value) && days.Value == Math.Truncate(days.Value)
                ? $"Saving removes {todos.ExpiredCount((int)days.Value)} completed task(s) already at least {days.Value:0} days old. Reopening a task cancels removal."
                : "Enter a whole number of days.";
        }
        forever.Click += (_, _) => Preview(); days.ValueChanged += (_, _) => Preview(); Preview();
        var save = Ui.Button("Save retention preference", () => TodoAction(() =>
        {
            if (forever.IsChecked != true && (!double.IsFinite(days.Value) || days.Value != Math.Truncate(days.Value))) throw new ArgumentException("Enter a whole number of days.");
            todos.SetRetention(forever.IsChecked == true ? null : (int)days.Value);
            Preview(); result.Text = todos.SaveError.Length == 0 ? "Preference saved." : todos.SaveError;
        }));
        return ToolLayout.Page("To-do settings", "Task timers and completed-task retention.",
            ToolLayout.Stack(
                ToolLayout.Section("Timer notifications", options,
                    Ui.Text("A persistent banner and To-do tab indicator appear when time runs out. When ProtoBench is in the background or minimized, a silent Windows notification and taskbar attention signal are requested. Clicking the notification opens the task. Windows notification settings and Do not disturb may suppress desktop banners."),
                    Ui.Text("Timers continue while you switch tabs or minimize ProtoBench. Closing the app stops alerts; any elapsed timer is shown when you next open it."), Ui.Text(desktopNotificationStatus)),
                ToolLayout.Section("Completed tasks", forever, days, impact, save, result)));
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }
    private void FlashTaskbar(bool attention)
    {
        var info = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = WinRT.Interop.WindowNative.GetWindowHandle(this), Flags = attention ? 2u : 0u, Count = attention ? 3u : 0u };
        FlashWindowEx(ref info);
    }
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
