using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Controls.Primitives;
using ProtoBench.Application.Services;
using ProtoBench.Application.Workspaces;
using ProtoBench.Core.Documents;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;

namespace ProtoBench.Desktop.Features.Todo;

public sealed class TodoViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "todo";
    public FrameworkElement Create(ToolWorkspace workspace) => new TodoView((TodoWorkspace)workspace);
}

internal sealed class TodoView : UserControl
{
    private readonly TodoWorkspace workspace;
    private TodoManager Tasks => workspace.Tasks;
    private readonly StackPanel list = new() { Spacing = 3, Margin = new Thickness(8) };
    private readonly StackPanel detail = new() { Spacing = 12 };
    private readonly StackPanel detailPinned = new() { Spacing = 10 };
    private readonly TextBlock error = Ui.Text("");
    private int selectedView;
    private readonly ToggleButton[] filters = new ToggleButton[3];
    private readonly TextBox capture = new() { PlaceholderText = "What needs doing?", MaxLength = 200, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer saveDelay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly Dictionary<Guid, Draft> drafts = [];
    private sealed class Draft { public string? Title; public string? Notes; }
    private TextBlock? countdown;
    private TextBlock? timerState;
    private Button? startPause;
    private bool localChange;
    private Guid? rendered;

    public TodoView(TodoWorkspace workspace)
    {
        this.workspace = workspace;
        if (workspace.SelectedTask is { } selected)
            selectedView = Tasks.Items.FirstOrDefault(t => t.Id == selected)?.CompletedAt is not null ? 2 : 1;
        var add = Ui.Primary(Ui.Button("＋ Add task", Add));
        add.VerticalAlignment = VerticalAlignment.Center;
        capture.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { Add(); e.Handled = true; } };
        var entry = new Grid { ColumnSpacing = 8 };
        entry.ColumnDefinitions.Add(new()); entry.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        entry.Children.Add(capture); Grid.SetColumn(add, 1); entry.Children.Add(add);
        var views = new ActionRow();
        for (var i = 0; i < filters.Length; i++)
        {
            var index = i;
            var button = (ToggleButton)XamlReader.Load("""
                <ToggleButton xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Padding="12,7" BorderThickness="0">
                    <ToggleButton.Resources>
                        <SolidColorBrush x:Key="ToggleButtonBackground" Color="Transparent" />
                        <SolidColorBrush x:Key="ToggleButtonBackgroundChecked" Color="{ThemeResource SystemAccentColor}" Opacity="0.14" />
                        <SolidColorBrush x:Key="ToggleButtonBackgroundCheckedPointerOver" Color="{ThemeResource SystemAccentColor}" Opacity="0.20" />
                        <SolidColorBrush x:Key="ToggleButtonBackgroundCheckedPressed" Color="{ThemeResource SystemAccentColor}" Opacity="0.25" />
                    </ToggleButton.Resources>
                </ToggleButton>
                """);
            button.Click += (_, _) => { FlushDrafts(); selectedView = index; Refresh(true); };
            filters[i] = button; views.Children.Add(button);
        }
        // Constrain both panes to the remaining window height. A page-level
        // ScrollViewer would measure the details at their full content height.
        var split = new Grid();
        split.ColumnDefinitions.Add(new() { Width = new GridLength(1.15, GridUnitType.Star) });
        split.ColumnDefinitions.Add(new());
        split.RowDefinitions.Add(new());
        split.RowDefinitions.Add(new() { Height = new GridLength(0) });
        var listViewport = PaneViewport(list, "Task list");
        var detailViewport = PaneViewport(detail, "Task details");
        var detailBorder = Surface("DividerStrokeColorDefaultBrush", false);
        var detailLayout = new Grid { RowSpacing = 12 };
        detailLayout.RowDefinitions.Add(new() { Height = GridLength.Auto }); detailLayout.RowDefinitions.Add(new());
        detailLayout.Children.Add(detailPinned); Grid.SetRow(detailViewport, 1); detailLayout.Children.Add(detailViewport);
        detailBorder.Padding = new Thickness(16); detailBorder.Child = detailLayout;
        split.Children.Add(listViewport); split.Children.Add(detailBorder); Grid.SetColumn(detailBorder, 1);
        split.SizeChanged += (_, _) =>
        {
            var narrow = split.ActualWidth < 660;
            split.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            split.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
            split.RowDefinitions[1].Height = narrow ? new GridLength(1.4, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(detailBorder, narrow ? 0 : 1); Grid.SetRow(detailBorder, narrow ? 1 : 0);
            detailBorder.BorderThickness = narrow ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        };
        var joined = Surface("CardStrokeColorDefaultBrush", true); joined.Child = split;
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new()); heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var title = Ui.Text("To-do", 26); title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        heading.Children.Add(new StackPanel { Spacing = 4, Children = { title, Secondary("Keep the next steps beside your tools.") } });
        var local = Secondary("Local workspace"); local.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(local, 1); heading.Children.Add(local);
        heading.SizeChanged += (_, _) => local.Visibility = heading.ActualWidth < 500 ? Visibility.Collapsed : Visibility.Visible;
        var page = new Grid { Margin = new Thickness(24), RowSpacing = 18 };
        var children = new FrameworkElement[] { heading, entry, views, error, joined, Secondary("Tasks are saved locally and shared across To-do tabs.") };
        for (var i = 0; i < children.Length; i++)
        {
            page.RowDefinitions.Add(new() { Height = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetRow(children[i], i); page.Children.Add(children[i]);
        }
        SizeChanged += (_, _) =>
        {
            var compact = ActualHeight < 660 || ActualWidth < 640;
            page.Margin = new Thickness(compact ? 16 : 24);
            page.RowSpacing = compact ? 12 : 18;
        };
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        Content = page;
        Loaded += (_, _) =>
        {
            // TabView's presenter otherwise permits a workspace to size to its content.
            for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ContentPresenter { Name: "TabContentPresenter" } presenter)
                {
                    presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
                    presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    break;
                }
            Tasks.Changed += Changed; workspace.Closing += FlushDrafts; Refresh(true); clock.Start();
        };
        Unloaded += (_, _) => { Tasks.Changed -= Changed; workspace.Closing -= FlushDrafts; clock.Stop(); saveDelay.Stop(); FlushDrafts(); };
        clock.Tick += (_, _) => PaintTimer();
        saveDelay.Tick += (_, _) => { saveDelay.Stop(); FlushDrafts(); };
    }
    private static FrameworkElement PaneViewport(UIElement content, string name)
    {
        var viewport = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(viewport, name);
        var host = new Grid();
        host.RowDefinitions.Add(new());
        host.RowDefinitions.Add(new() { Height = GridLength.Auto });
        host.Children.Add(viewport);
        var hintText = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        var hint = new HyperlinkButton
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(8, 4, 8, 4), Content = hintText
        };
        var hintBorder = Surface("DividerStrokeColorDefaultBrush", false);
        hintBorder.BorderThickness = new Thickness(0, 1, 0, 0);
        hintBorder.Child = hint;
        hintBorder.Visibility = Visibility.Collapsed;
        Grid.SetRow(hintBorder, 1); host.Children.Add(hintBorder);
        var subject = name == "Task list" ? "tasks" : "details";
        void UpdateHint()
        {
            // Compare against the full available pane, so reserving the hint's
            // own height cannot create overflow and keep the hint alive by itself.
            var overflows = host.ActualHeight > 0 && viewport.ExtentHeight > host.ActualHeight + 1;
            var visibility = overflows ? Visibility.Visible : Visibility.Collapsed;
            if (hintBorder.Visibility != visibility) hintBorder.Visibility = visibility;
            if (!overflows) return;
            var above = viewport.VerticalOffset > 1;
            var below = viewport.ScrollableHeight - viewport.VerticalOffset > 1;
            var label = below ? (above ? "↑ More above · " : "") + $"Scroll for more {subject} ↓"
                : $"↑ Scroll for earlier {subject}";
            if (hintText.Text != label)
            {
                hintText.Text = label;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(hint, label);
            }
        }
        // Extents can change after task selection, text wrapping, or window resizing.
        host.LayoutUpdated += (_, _) => UpdateHint();
        viewport.ViewChanged += (_, _) => UpdateHint();
        hint.Click += (_, _) =>
        {
            var below = viewport.ScrollableHeight - viewport.VerticalOffset > 1;
            var distance = Math.Max(40, viewport.ViewportHeight * .8);
            viewport.ChangeView(null, Math.Clamp(viewport.VerticalOffset + (below ? distance : -distance), 0, viewport.ScrollableHeight), null);
        };
        return host;
    }
    private static Border Surface(string border, bool filled) => (Border)XamlReader.Load($$"""
        <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
            BorderBrush="{ThemeResource {{border}}}" BorderThickness="{{(filled ? "1" : "0")}}" CornerRadius="{{(filled ? "6" : "0")}}"
            Background="{{(filled ? "{ThemeResource LayerFillColorDefaultBrush}" : "Transparent")}}" />
        """);
    private static TextBlock Secondary(string text)
    {
        var block = (TextBlock)XamlReader.Load("""
            <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Foreground="{ThemeResource TextFillColorSecondaryBrush}" FontSize="12" TextWrapping="Wrap" />
            """);
        block.Text = text; return block;
    }
    private void Run(Action action)
    {
        try { localChange = true; action(); error.Text = Tasks.SaveError; }
        catch (Exception ex) { error.Text = ex.Message; }
        finally { localChange = false; }
    }
    private void Add()
    {
        Run(() => { Tasks.Add(capture.Text); workspace.SelectedTask = Tasks.Items.Last().Id; capture.Text = ""; selectedView = 0; });
        Refresh(true);
    }
    private void Changed() => Refresh(!localChange);
    private void Refresh(bool rebuildDetail)
    {
        error.Text = Tasks.SaveError;
        list.Children.Clear();
        if (!Tasks.IsLoaded) { list.Children.Add(Ui.Text("Task storage is unavailable. Correct the task file and restart the app.")); return; }
        var today = DateOnly.FromDateTime(DateTime.Now);
        var counts = new[] { Tasks.Items.Count(t => t.CompletedAt is null && t.DueDate <= today), Tasks.Items.Count(t => t.CompletedAt is null), Tasks.Items.Count(t => t.CompletedAt is not null) };
        var labels = new[] { "Today", "All tasks", "Completed" };
        for (var i = 0; i < filters.Length; i++)
        {
            var label = (TextBlock)XamlReader.Load($$"""
                <TextBlock xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Foreground="{ThemeResource {{(selectedView == i ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush")}}}" />
                """);
            label.Text = $"{labels[i]}  {counts[i]}";
            filters[i].Content = label; filters[i].IsChecked = selectedView == i;
        }
        list.Children.Add(new Border { Padding = new Thickness(10, 10, 10, 8), Child = Secondary(selectedView == 0 ? "Due today and overdue" : selectedView == 1 ? "Open tasks" : "Completed tasks") });
        var visible = Tasks.Items.Where(t => selectedView == 2 ? t.CompletedAt is not null :
            t.CompletedAt is null && (selectedView == 1 || t.DueDate <= today)).OrderByDescending(t => t.HighPriority).ThenBy(t => t.DueDate).ToArray();
        if (!visible.Any(t => t.Id == workspace.SelectedTask)) workspace.SelectedTask = visible.FirstOrDefault()?.Id;
        foreach (var task in visible)
        {
            var complete = new CheckBox { IsChecked = task.CompletedAt is not null, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0, Width = 28 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(complete, "Complete " + task.Title);
            complete.Click += (_, _) => { Run(() => Tasks.Complete(task.Id, complete.IsChecked == true)); Refresh(true); };
            var subtitle = task.CompletedAt is { } at ? $"Completed {at.ToLocalTime():d}" :
                (task.DueDate is { } due ? $"Due {due:d}" : "No due date") + (task.HighPriority ? " · High priority" : "") +
                (task.NeedsAttention ? " · Time’s up" : "") + (task.Steps.Count > 0 ? $" · {task.Steps.Count(s => s.Completed)}/{task.Steps.Count} steps" : "");
            var open = new Button { Content = new StackPanel { Spacing = 5, Children = { Ui.Text(task.Title), Secondary(subtitle) } },
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0), MinWidth = 0 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, task.Title);
            open.Click += (_, _) => { workspace.SelectedTask = task.Id; Refresh(true); };
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new());
            row.Children.Add(complete); Grid.SetColumn(open, 1); row.Children.Add(open);
            var selectedRow = task.Id == workspace.SelectedTask;
            var rowBorder = (Border)XamlReader.Load($"""
                <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" Padding="10,12"
                    CornerRadius="4" BorderThickness="3,0,0,0"
                    BorderBrush="{(selectedRow ? "{ThemeResource AccentFillColorDefaultBrush}" : "Transparent")}"
                    Background="{(selectedRow ? "{ThemeResource SubtleFillColorSecondaryBrush}" : "Transparent")}" />
                """);
            rowBorder.Child = row; list.Children.Add(rowBorder);
        }
        if (visible.Length == 0) list.Children.Add(new Border { Padding = new Thickness(10, 24, 10, 24), Child = Secondary("No tasks in this view. Add a task above.") });
        // A timer ending or another tab changing must not replace an editor under the user's cursor.
        var editing = false;
        if (IsLoaded && XamlRoot is not null)
        {
            var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            var inEditor = false;
            for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is TextBox or NumberBox or CalendarDatePicker) inEditor = true;
                if (ReferenceEquals(node, detail)) { editing = inEditor; break; }
            }
        }
        if ((rebuildDetail && !editing) || rendered != workspace.SelectedTask) BuildDetail();
        PaintTimer();
    }
    private void Edit(Func<TodoItem, TodoItem> update)
    {
        if (workspace.SelectedTask is { } id) Run(() => Tasks.Update(update(Tasks.Get(id))));
    }
    private void QueueDraft(Guid id, string? title = null, string? notes = null)
    {
        if (!drafts.TryGetValue(id, out var draft)) drafts[id] = draft = new();
        if (title is not null) draft.Title = title;
        if (notes is not null) draft.Notes = notes;
        saveDelay.Stop(); saveDelay.Start();
    }
    private void FlushDrafts()
    {
        var pending = drafts.ToArray(); drafts.Clear();
        foreach (var (id, draft) in pending)
        {
            if (!Tasks.Items.Any(t => t.Id == id)) continue;
            Run(() => { var t = Tasks.Get(id); Tasks.Update(t with { Title = draft.Title?.Trim() ?? t.Title, Notes = draft.Notes ?? t.Notes }); });
        }
    }
    private void BuildDetail()
    {
        detail.Children.Clear(); detailPinned.Children.Clear(); rendered = workspace.SelectedTask; countdown = timerState = null; startPause = null;
        var detailHeading = Ui.Text("Task details", 16); detailHeading.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        detailPinned.Children.Add(detailHeading);
        if (rendered is not { } id) { detail.Children.Add(Secondary("Select a task to see its details.")); return; }
        var task = Tasks.Get(id);
        var title = new TextBox { Header = "Task", Text = task.Title, MaxLength = 200 };
        title.TextChanged += (_, _) => QueueDraft(id, title: title.Text);
        title.LostFocus += (_, _) => FlushDrafts();
        var due = new CalendarDatePicker { Header = "Due date", Date = task.DueDate is { } date ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue)) : null, HorizontalAlignment = HorizontalAlignment.Stretch };
        due.DateChanged += (_, _) => Edit(t => t with { DueDate = due.Date is { } d ? DateOnly.FromDateTime(d.Date) : null });
        var clearDate = new HyperlinkButton { Content = "Clear due date", Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left };
        clearDate.Click += (_, _) => due.Date = null;
        var priority = new ComboBox { Header = "Priority", ItemsSource = new[] { "Normal", "High" }, SelectedIndex = task.HighPriority ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        priority.SelectionChanged += (_, _) => Edit(t => t with { HighPriority = priority.SelectedIndex == 1 });
        var minutes = new NumberBox { Header = "Minutes", Width = 110, Minimum = 1, Maximum = 480, Value = task.TimeboxMinutes,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, IsEnabled = task.CompletedAt is null && task.Deadline is null };
        minutes.ValueChanged += (_, _) => { if (double.IsFinite(minutes.Value) && minutes.Value == Math.Truncate(minutes.Value)) Run(() => Tasks.SetDuration(id, (int)minutes.Value)); PaintTimer(); };
        countdown = Ui.Text("", 32);
        countdown.VerticalAlignment = VerticalAlignment.Center;
        timerState = Secondary("");
        startPause = Ui.Button("Start", () =>
        {
            Run(() => { if (Tasks.Get(id).Deadline is null) Tasks.Start(id); else Tasks.Pause(id); });
            minutes.IsEnabled = Tasks.Get(id).Deadline is null; PaintTimer();
        });
        startPause.IsEnabled = task.CompletedAt is null;
        var reset = Ui.Button("Reset", () => { Run(() => Tasks.Reset(id)); minutes.IsEnabled = Tasks.Get(id).CompletedAt is null; });
        var notes = new TextBox { Header = "Notes", Text = task.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 90, MaxLength = 10000 };
        notes.TextChanged += (_, _) => QueueDraft(id, notes: notes.Text);
        notes.LostFocus += (_, _) => FlushDrafts();
        detail.Children.Add(title); detail.Children.Add(ToolLayout.Columns(190, due, priority)); detail.Children.Add(clearDate);
        var timer = Surface("CardStrokeColorDefaultBrush", true); timer.Padding = new Thickness(12);
        var timerHeading = new Grid { ColumnSpacing = 8 };
        timerHeading.ColumnDefinitions.Add(new()); timerHeading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        timerHeading.Children.Add(Ui.Text("Timebox", 14)); Grid.SetColumn(timerState, 1); timerHeading.Children.Add(timerState);
        var timerValues = new Grid { ColumnSpacing = 12 };
        timerValues.ColumnDefinitions.Add(new()); timerValues.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        timerValues.Children.Add(countdown); Grid.SetColumn(minutes, 1); timerValues.Children.Add(minutes);
        timer.Child = new StackPanel { Spacing = 8, Children = { timerHeading, timerValues, Ui.Row(Ui.Primary(startPause), reset) } };
        ToolTipService.SetToolTip(timer, "One timer at a time. Time’s up leaves the task open.");
        if (task.CompletedAt is null) detailPinned.Children.Add(timer);
        else detailPinned.Children.Add(Secondary($"Completed · {task.TimeboxMinutes}-minute timebox"));
        detail.Children.Add(notes); detail.Children.Add(Ui.Text("Checklist", 16));
        foreach (var step in task.Steps)
        {
            var box = new CheckBox { Content = Ui.Text(step.Text), IsChecked = step.Completed };
            box.Click += (_, _) => Edit(t => t with { Steps = t.Steps.Select(s => s.Id == step.Id ? s with { Completed = box.IsChecked == true } : s).ToList() });
            var removeStep = Ui.Button("Remove", () => { Edit(t => t with { Steps = t.Steps.Where(s => s.Id != step.Id).ToList() }); BuildDetail(); });
            detail.Children.Add(Ui.Row(box, removeStep));
        }
        var newStep = new TextBox { PlaceholderText = "Add a step", MaxLength = 200 };
        detail.Children.Add(Ui.Row(newStep, Ui.Button("Add step", () =>
        {
            if (string.IsNullOrWhiteSpace(newStep.Text)) return;
            Edit(t => t with { Steps = [.. t.Steps, new TodoStep(Guid.NewGuid(), newStep.Text.Trim())] }); BuildDetail();
        })));
        if (task.CompletedAt is not null) detail.Children.Add(Ui.Text($"Completed {task.CompletedAt.Value.ToLocalTime():g}", 12));
        var remove = Ui.AsyncButton("Remove task…", async () =>
        {
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Remove this task?", Content = Tasks.Get(id).Title,
                PrimaryButtonText = "Remove", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) { Run(() => Tasks.Remove(id)); Refresh(true); }
        }, ex => error.Text = ex.Message);
        detail.Children.Add(remove);
    }
    private void PaintTimer()
    {
        if (rendered is not { } id || countdown is null || !Tasks.Items.Any(t => t.Id == id)) return;
        var t = Tasks.Get(id); var seconds = (int)Math.Ceiling(Tasks.Remaining(t));
        countdown.Text = $"{seconds / 60:00}:{seconds % 60:00}";
        if (timerState is not null) timerState.Text = t.NeedsAttention ? "Time’s up" : t.Deadline is not null ? "Focusing" : seconds < t.TimeboxMinutes * 60 ? "Paused" : "Ready";
        if (startPause is not null) startPause.Content = t.Deadline is not null ? "Pause" : seconds == 0 ? "Start again" : seconds < t.TimeboxMinutes * 60 ? "Resume" : "Start";
    }
}
