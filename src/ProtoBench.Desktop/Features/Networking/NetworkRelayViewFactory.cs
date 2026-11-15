using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Networking;

public sealed class NetworkRelayViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "network-relay";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (NetworkRelayWorkspace)workspace;
        var feedback = Ui.Text("");
        void Copy(string text)
        {
            if (text.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); feedback.Text = "Copied"; }
            catch { feedback.Text = "Clipboard unavailable. Select and copy the data manually."; }
        }
        TextBox Input(string property, string header, int limit = 253)
        {
            var box = Ui.Input(vm, property, header); box.MaxLength = limit; return box;
        }
        CheckBox Check(string property, string label)
        {
            var check = new CheckBox { Content = label };
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.TwoWay });
            return check;
        }
        var incoming = ToolLayout.Stack(Check(nameof(vm.IncomingTls), "TLS from client (decrypt incoming traffic)"));
        var certificate = Input(nameof(vm.CertificatePath), "Incoming server certificate · PFX/P12 file", 1024);
        var password = new PasswordBox { Header = "PFX/P12 password", MaxLength = 1024, PasswordRevealMode = PasswordRevealMode.Peek };
        password.PasswordChanged += (_, _) => vm.CertificatePassword = password.Password;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CertificatePassword) && password.Password != vm.CertificatePassword) password.Password = vm.CertificatePassword; };
        incoming.Children.Add(new Expander
        {
            Header = "Incoming TLS certificate", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = ToolLayout.Stack(Ui.Text("Create a temporary certificate, or load your own PFX/P12. Configure your test client to trust the certificate and connect using its DNS name/IP."),
                Input(nameof(vm.CertificateName), "Test certificate DNS name or IP"),
                Ui.Button("Create test certificate", vm.CreateTestCertificate),
                Ui.BoundText(vm, nameof(vm.CertificateInfo)),
                Ui.AsyncButton("Export public test certificate…", async () =>
                {
                    var path = await dialogs.SaveAsync("protobench-test-certificate", ".pem", "PEM certificate");
                    if (path is not null) await vm.ExportTestCertificateAsync(path);
                }, ex => feedback.Text = ex.Message),
                certificate, Ui.AsyncButton("Browse PFX/P12…", async () => { var path = await dialogs.OpenAsync(); if (path is not null) vm.CertificatePath = path; }, ex => feedback.Text = ex.Message), password)
        });
        var upstream = ToolLayout.Stack(Check(nameof(vm.UpstreamTls), "TLS to upstream server"),
            new Expander
            {
                Header = "Upstream TLS options", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = ToolLayout.Stack(Input(nameof(vm.TlsServerName), "TLS server name / SNI (blank = upstream host)"),
                    Input(nameof(vm.UpstreamRootPath), "Optional custom trusted CA · PEM/DER file", 1024),
                    Ui.AsyncButton("Browse trusted CA…", async () => { var path = await dialogs.OpenAsync(); if (path is not null) vm.UpstreamRootPath = path; }, ex => feedback.Text = ex.Message),
                    Ui.Text("Validates the upstream certificate and hostname using system trust, or only the selected CA. Revocation is not checked."))
            });
        var configuration = ToolLayout.Columns(300,
            ToolLayout.Stack(
                ToolLayout.Columns(140, Input(nameof(vm.ListenAddress), "Listen IP"), Input(nameof(vm.ListenPort), "Listen port", 5)), incoming),
            ToolLayout.Stack(
                ToolLayout.Columns(140, Input(nameof(vm.UpstreamHost), "Upstream host / IP"), Input(nameof(vm.UpstreamPort), "Upstream port", 5)), upstream));
        var configHost = new ContentControl { Content = configuration, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        configHost.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var start = new Button { Content = "Start relay" };
        start.Click += async (_, _) => { try { await vm.StartAsync(); } catch (Exception ex) { feedback.Text = ex.Message; } };
        start.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var stop = new Button { Content = "Stop relay" };
        stop.Click += async (_, _) => { try { await vm.StopAsync(); } catch (Exception ex) { feedback.Text = ex.Message; } };
        stop.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.IsRunning)) });
        var entries = new ListView
        {
            ItemsSource = vm.Entries, Height = 220, SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)XamlReader.Load("""
                <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                    <TextBlock Text="{Binding Summary}" TextWrapping="Wrap" Margin="0,4" />
                </DataTemplate>
                """),
            ItemContainerStyle = new Style(typeof(ListViewItem))
            {
                Setters = { new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch) }
            }
        };
        entries.SetBinding(ListView.SelectedItemProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Selected)), Mode = BindingMode.TwoWay });
        RawTextViewer Result(string property, string header)
        {
            var viewer = new RawTextViewer { Header = header, MinHeight = 100, MaxHeight = 220, TextWrapping = TextWrapping.Wrap };
            viewer.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
            return viewer;
        }
        var dataTabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent };
        dataTabs.TabItems.Add(new TabViewItem { Header = "Hex", IsClosable = false,
            Content = ToolLayout.Stack(Result(nameof(vm.SelectedHex), "Exact received bytes (hex)"), Ui.Button("Copy hex", () => Copy(vm.SelectedHex))) });
        dataTabs.TabItems.Add(new TabViewItem { Header = "Text", IsClosable = false,
            Content = ToolLayout.Stack(Result(nameof(vm.SelectedText), "UTF-8 preview (replacement for invalid/split characters)"), Ui.Button("Copy text", () => Copy(vm.SelectedText))) });
        dataTabs.SelectedIndex = 0;
        var page = ToolLayout.Page(vm.Title, "Client → relay → server. Inspect both directions, including decrypted TLS application bytes.",
            ToolLayout.Section("Connection", configHost, Ui.Row(start, stop), Ui.BoundText(vm, nameof(vm.Status)), Ui.BoundText(vm, nameof(vm.Error))),
            ToolLayout.Section("Traffic", Ui.BoundText(vm, nameof(vm.Summary)), entries, Ui.BoundText(vm, nameof(vm.SelectedInfo)), dataTabs,
                ToolLayout.Columns(150, Ui.Button("Clear capture", vm.ClearCapture), Ui.AsyncButton("Export capture…", async () =>
                {
                    var path = await dialogs.SaveAsync("tcp-relay-capture", ".jsonl", "JSON Lines capture");
                    if (path is not null) await vm.ExportCaptureAsync(path);
                }, ex => feedback.Text = ex.Message)), feedback),
            ToolLayout.Help(Ui.Text("Point your client at the listen IP/port and set the real server as upstream. Default bind is local-only; use a local LAN IP or 0.0.0.0 to accept remote clients. No firewall rules are changed. Incoming TLS and upstream TLS are independent (plain↔plain, TLS↔TLS or TLS↔plain). TLS 1.2/1.3 are supported; legacy SSL, STARTTLS, UDP, HTTP CONNECT, mutual TLS and traffic editing are not supported. Test certificates last 30 days; export the public certificate to configure client trust. Windows TLS uses temporary private key containers whose cleanup is managed by .NET; no trusted certificates are installed. Pinning requires your client's test configuration to accept this certificate. At most 16 simultaneous connections; connect/handshake timeout 30 seconds. TCP read chunks are not protocol message boundaries. Captures show received bytes; forwarding errors appear as events. Old entries are evicted at 1,024 entries / 4 MiB while forwarding continues. Export saves the currently retained entries as JSON Lines with exact hex bytes. Closing this tab stops the listener and connections. Passwords, settings and traffic are not persisted automatically.")));
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => vm.RefreshCapture();
        page.Loaded += (_, _) => timer.Start();
        page.Unloaded += (_, _) => timer.Stop();
        return page;
    }
}
