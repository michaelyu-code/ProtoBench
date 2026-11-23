using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Networking;

public sealed class TestTelnetServerViewFactory : IWorkspaceViewFactory
{
    public string ToolId => "test-telnet-server";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (TestTelnetServerWorkspace)workspace;
        var required = new RequiredInputs(vm);
        var address = Ui.Input(vm, nameof(vm.ListenAddress), "Listen IP"); address.MaxLength = 45;
        var port = Ui.Input(vm, nameof(vm.ListenPort), "Listen port (0 = automatic)"); port.MaxLength = 5;
        required.Add(address, () => System.Net.IPAddress.TryParse(vm.ListenAddress.Trim(), out _) ? null : "Enter a local listen IP address.");
        required.Add(port, () => int.TryParse(vm.ListenPort, out var value) && value is >= 0 and <= 65535 ? null : "Enter a port from 0 to 65535.");
        var profile = new ComboBox
        {
            Header = "Server profile", ItemsSource = vm.ServerProfiles,
            SelectedIndex = vm.SelectedProfileIndex, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        profile.SelectionChanged += (_, _) => { if (profile.SelectedIndex >= 0) vm.SelectedProfileIndex = profile.SelectedIndex; };
        var ansi = new CheckBox { Content = "Include ANSI sample in greeting" };
        ansi.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.AnsiGreeting)), Mode = BindingMode.TwoWay });
        void UpdateProfile() => ansi.Visibility = vm.ShowAnsiGreeting ? Visibility.Visible : Visibility.Collapsed;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.SelectedProfileIndex)) return;
            profile.SelectedIndex = vm.SelectedProfileIndex; UpdateProfile();
        };
        UpdateProfile();
        var tls = new CheckBox { Content = "Use TLS" };
        tls.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.UseTls)), Mode = BindingMode.TwoWay });
        var password = new PasswordBox { Header = "PFX password", Password = vm.CertificatePassword };
        password.PasswordChanged += (_, _) => vm.CertificatePassword = password.Password;
        var hostname = Ui.Input(vm, nameof(vm.CertificateHostname), "Test certificate hostname / IP");
        required.Require(hostname, "Enter a hostname or IP for the test certificate.", () => vm.UseTls && string.IsNullOrWhiteSpace(vm.CertificatePath));
        var tlsSettings = ToolLayout.Stack(
            Ui.Input(vm, nameof(vm.CertificatePath), "PFX path (optional)"), password,
            hostname,
            Ui.Text("Leave the PFX path empty to generate a temporary self-signed certificate. Clients must trust that certificate or allow it for testing. TLS starts immediately on connection."));
        void UpdateTls() => tlsSettings.Visibility = vm.UseTls ? Visibility.Visible : Visibility.Collapsed;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.UseTls)) UpdateTls(); };
        UpdateTls();
        var config = new ContentControl
        {
            Content = ToolLayout.Stack(profile, new Expander { Header = "Profile details", Content = Ui.BoundText(vm, nameof(vm.ProfileDescription)),
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch }, ToolLayout.Columns(160, address, port), ansi, tls, tlsSettings),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        config.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var configuration = new Expander { Header = "Server configuration", IsExpanded = true, Content = config,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CanConfigure)) configuration.IsExpanded = vm.CanConfigure; };
        var start = Ui.Primary(Ui.Button("Start server", () => required.Run(vm.Start)));
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
                    Ui.Text("Relay setup: use this server's listen IP and actual port as the relay's upstream endpoint. Set the relay upstream to match this server: Plain TCP or TLS. For a generated test certificate, adjust upstream certificate trust for testing. Choose the relay incoming mode to match your client. Connect your Telnet client to the relay's listen port. Use different ports; for example server 2323 and relay 9000. If the server binds 0.0.0.0 or ::, use 127.0.0.1 or ::1 as the local upstream IP."))
            };
        var page = ToolLayout.Page(vm.Title,
            "Test server with built-in and plugin profiles, optional TLS, and up to 8 clients.",
            ToolLayout.Columns(340,
                ToolLayout.Section("Server", Ui.Row(start, stop), endpoint, copyEndpoint, configuration,
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
