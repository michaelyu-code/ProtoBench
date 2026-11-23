using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;

namespace ProtoBench.Desktop.Features.Keyboard;

public sealed class TimedKeyPressViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "timed-key-presser";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (TimedKeyPressWorkspace)workspace;
        var required = new RequiredInputs(vm);
        var key = new ComboBox { Header = "Key to press", ItemsSource = vm.Keys, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        key.SetBinding(ComboBox.SelectedItemProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.SelectedKey)), Mode = BindingMode.TwoWay });
        var seconds = Ui.Input(vm, nameof(vm.IntervalSeconds), "Interval (seconds)");
        seconds.MaxLength = 16;
        required.Require(key, "Select a key to press.");
        required.Add(seconds, () => !double.TryParse(vm.IntervalSeconds, out var interval) || !double.IsFinite(interval) || interval is < 1 or > 86400
            ? "Enter an interval from 1 to 86,400 seconds." : null);
        var randomTiming = new CheckBox { Content = "Randomize time between key presses" };
        randomTiming.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.RandomizeTiming)), Mode = BindingMode.TwoWay });
        var inputs = new StackPanel { Spacing = 8 };
        inputs.Children.Add(ToolLayout.Columns(180, key, seconds));
        inputs.Children.Add(randomTiming);
        var configuration = new ContentControl { Content = inputs, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        configuration.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var toggle = Ui.Primary(Ui.Button("Start", () => { if (vm.IsRunning) vm.Toggle(); else required.Run(vm.Toggle); }));
        toggle.SetBinding(ContentControl.ContentProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.ToggleLabel)) });
        var activity = new RawTextViewer { CompactViewport = false, MinHeight = 0, MaxHeight = double.PositiveInfinity,
            VerticalAlignment = VerticalAlignment.Stretch, TextWrapping = TextWrapping.Wrap };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(activity, "Activity");
        activity.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Activity)), Mode = BindingMode.OneWay });
        static ScrollViewer? FindScroll(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ScrollViewer scroll) return scroll;
                if (FindScroll(child) is { } nested) return nested;
            }
            return null;
        }
        void ScrollToEnd() => activity.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (!activity.IsLoaded) return;
            activity.UpdateLayout();
            if (FindScroll(activity) is { } scroll) scroll.ChangeView(null, scroll.ScrollableHeight, null, disableAnimation: true);
        });
        activity.TextChanged += (_, _) => ScrollToEnd();
        activity.Loaded += (_, _) => ScrollToEnd();
        activity.SizeChanged += (_, _) => ScrollToEnd();
        // This timer belongs to the workspace, not the visible tab. Switching
        // tabs leaves it running; Dispose changes IsRunning and stops it.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => vm.Tick();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.IsRunning)) return;
            if (vm.IsRunning) timer.Start(); else timer.Stop();
        };
        var schedule = new ScrollViewer
        {
            Content = ToolLayout.Section("Schedule", configuration, Ui.Text("200 seconds = 3 minutes 20 seconds. Normally, the first press waits one full interval. With random timing, each press waits a new random delay from 1 second up to the interval entered. The selected key stays the same."),
                toggle, Ui.BoundText(vm, nameof(vm.Error)), Ui.BoundText(vm, nameof(vm.Status))),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var activityLayout = new Grid { RowSpacing = 8 };
        activityLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        activityLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        activityLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        activityLayout.Children.Add(Ui.Text("Activity", 18));
        var summary = Ui.BoundText(vm, nameof(vm.Summary));
        Grid.SetRow(summary, 1); activityLayout.Children.Add(summary);
        Grid.SetRow(activity, 2); activityLayout.Children.Add(activity);
        var activitySection = (Border)ToolLayout.Section("");
        activitySection.Child = activityLayout;
        var helpContent = new ScrollViewer
        {
            Content = Ui.Text("Keys are sent to whichever app is in focus. Choose that app after starting. Windows supports F1–F24; F25 is not available. Inputs are locked while running. Switching tabs or minimizing ProtoBench keeps the schedule active; Stop, closing this tab or exiting the app stops it. Timing is approximate and pauses if Windows suspends the app; missed presses are not replayed. Each interval sends the selected key regardless of held keys, without changing modifier state. Elevated apps may reject presses. The latest 1,000 log entries stay in this tab and are not saved."),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var page = new Grid { Margin = new Thickness(12), RowSpacing = 8 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = Ui.Heading(vm.Title);
        page.Children.Add(heading);
        Grid.SetRow(schedule, 1); page.Children.Add(schedule);
        Grid.SetRow(activitySection, 2); page.Children.Add(activitySection);
        var help = ToolLayout.Help(helpContent);
        Grid.SetRow(help, 3); page.Children.Add(help);
        page.SizeChanged += (_, _) =>
        {
            schedule.MaxHeight = Math.Max(60, page.ActualHeight * .48);
            helpContent.MaxHeight = Math.Max(40, page.ActualHeight * .25);
            heading.FontSize = page.ActualHeight < 640 || page.ActualWidth < 720 ? 22 : 28;
        };
        page.Loaded += (_, _) =>
        {
            for (DependencyObject? parent = VisualTreeHelper.GetParent(page); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ContentPresenter { Name: "TabContentPresenter" } presenter) continue;
                presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
                presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                break;
            }
        };
        return page;
    }
}
