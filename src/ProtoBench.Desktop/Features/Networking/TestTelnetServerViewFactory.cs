using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Networking;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Networking;

public sealed class TestTelnetServerViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "test-telnet-server";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (TestTelnetServerWorkspace)workspace;
        var address = Ui.Input(vm, nameof(vm.ListenAddress), "Listen IP"); address.MaxLength = 45;
        var port = Ui.Input(vm, nameof(vm.ListenPort), "Listen port (0 = automatic)"); port.MaxLength = 5;
        var profile = new ComboBox
        {
            Header = "Server profile", ItemsSource = new[] { "ASCII Telnet", "ANSI / VT100", "Telserv / TN6530 (conversational simulation)" },
            SelectedIndex = (int)vm.Profile, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        profile.SelectionChanged += (_, _) => { if (profile.SelectedIndex >= 0) vm.Profile = (TelnetServerProfile)profile.SelectedIndex; };
        var ansi = new CheckBox { Content = "Include ANSI sample in greeting" };
        ansi.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.AnsiGreeting)), Mode = BindingMode.TwoWay });
        void UpdateProfile() => ansi.Visibility = vm.ShowAnsiGreeting ? Visibility.Visible : Visibility.Collapsed;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.Profile)) return;
            profile.SelectedIndex = (int)vm.Profile; UpdateProfile();
        };
        UpdateProfile();
        var config = new ContentControl
        {
            Content = ToolLayout.Stack(profile, Ui.BoundText(vm, nameof(vm.ProfileDescription)), ToolLayout.Columns(160, address, port), ansi),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        config.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var start = Ui.Button("Start server", vm.Start);
        start.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var stop = new Button { Content = "Stop server" };
        stop.Click += async (_, _) => await vm.StopAsync();
        stop.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanStop)) });
        var feedback = Ui.Text("");
        var endpoint = new TextBox { Header = "Server endpoint", IsReadOnly = true };
        endpoint.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Endpoint)) });
        var activity = new RawTextViewer { Header = "Server activity", MinHeight = 240, MaxHeight = 360, TextWrapping = TextWrapping.Wrap };
        activity.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Activity)) });
        var copyEndpoint = Ui.Button("Copy endpoint", () =>
            {
                if (vm.Endpoint.Length == 0) return;
                try { var data = new DataPackage(); data.SetText(vm.Endpoint); Clipboard.SetContent(data); feedback.Text = "Endpoint copied"; }
                catch { feedback.Text = "Clipboard unavailable. Copy the endpoint manually."; }
            });
        var help = new Expander
            {
                Header = "Commands and relay setup", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = ToolLayout.Stack(
                    Ui.Text("Commands: help, echo <text>, ansi, time, quit. Other text is echoed back. Enable Telnet preview in the relay to clean the ANSI sample. Stop or close this tab to release the listener and clients. Sessions idle for 5 minutes are closed; input lines are limited to 1,024 characters."),
                    Ui.Text("Telserv/TN6530: configure the client terminal type as TN6530, TN653X, 6530, 653X or TN6530-8. Choose TACL at the service menu. Simulated commands: HELP, STATUS, TIME, VOLUME, FILES, ECHO <text>, SCREEN, LOGOFF. LOGOFF returns to the menu; EXIT at the menu disconnects. Terminal negotiation has a 30-second deadline. Clients declining terminal type use a text-only menu. This is a conversational test simulator; block-mode forms and host execution are not supported."),
                    Ui.Text("Relay setup: use this server's listen IP and actual port as the relay's upstream endpoint. Set both incoming and upstream connections to Plain TCP so the server greeting arrives immediately. Connect your Telnet client to the relay's listen port. Use different ports; for example server 2323 and relay 9000. If the server binds 0.0.0.0 or ::, use 127.0.0.1 or ::1 as the local upstream IP."))
            };
        var page = ToolLayout.Page(vm.Title,
            "Standalone test server with ASCII, ANSI/VT100 and Telserv/TN6530 simulation profiles. No login required; up to 8 clients.",
            ToolLayout.Columns(340,
                ToolLayout.Section("Server", config, Ui.Row(start, stop), endpoint, copyEndpoint,
                    Ui.BoundText(vm, nameof(vm.Status)), Ui.BoundText(vm, nameof(vm.Error)), feedback),
                ToolLayout.Section("Activity", Ui.BoundText(vm, nameof(vm.Summary)), activity, Ui.Button("Clear activity", vm.ClearActivity))),
            help);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => vm.Refresh();
        page.Loaded += (_, _) => timer.Start();
        page.Unloaded += (_, _) => timer.Stop();
        return page;
    }
}
