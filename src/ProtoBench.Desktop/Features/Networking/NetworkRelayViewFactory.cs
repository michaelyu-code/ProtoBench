using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Utilities.Text;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Networking;

public sealed class NetworkRelayViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "network-relay";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (NetworkRelayWorkspace)workspace;
        var fields = new Dictionary<string, TextBox>();
        var validationTips = new Dictionary<string, ToolTip>();
        var revealConfiguration = () => { };
        var feedback = Ui.Text("");
        feedback.MaxLines = 2; feedback.TextTrimming = TextTrimming.CharacterEllipsis;
        void Copy(string text)
        {
            if (text.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(text); Clipboard.SetContent(data); feedback.Text = "Copied"; }
            catch { feedback.Text = "Clipboard unavailable. Select and copy the data manually."; }
        }
        TextBox Input(string property, string header, int limit = 253)
        {
            var box = Ui.Input(vm, property, header); box.MaxLength = limit;
            fields[property] = box;
            box.TextChanged += (_, _) =>
            {
                if (validationTips.Remove(property, out var tip))
                {
                    tip.IsOpen = false;
                    ToolTipService.SetToolTip(box, null);
                }
            };
            return box;
        }
        CheckBox Check(string property, string label)
        {
            var check = new CheckBox { Content = label };
            check.SetBinding(CheckBox.IsCheckedProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.TwoWay });
            return check;
        }
        var incomingMode = new ComboBox
        {
            Header = "Incoming connection", ItemsSource = new[] { "Plain TCP", "TLS", "Auto · first byte 0x16 = TLS" },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        incomingMode.SetBinding(ComboBox.SelectedIndexProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.IncomingMode)), Mode = BindingMode.TwoWay });
        var incoming = ToolLayout.Stack(incomingMode);
        var certificate = Input(nameof(vm.CertificatePath), "Incoming server certificate · PFX/P12 file", 1024);
        var password = new PasswordBox { Header = "PFX/P12 password", MaxLength = 1024, PasswordRevealMode = PasswordRevealMode.Peek };
        password.PasswordChanged += (_, _) => vm.CertificatePassword = password.Password;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CertificatePassword) && password.Password != vm.CertificatePassword) password.Password = vm.CertificatePassword; };
        var certificateOptions = new Expander
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
        };
        incoming.Children.Add(certificateOptions);
        var upstreamMode = new ComboBox
        {
            Header = "Upstream connection", ItemsSource = new[] { "Plain TCP", "TLS", "Match incoming" },
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        upstreamMode.SetBinding(ComboBox.SelectedIndexProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.UpstreamMode)), Mode = BindingMode.TwoWay });
        var upstream = ToolLayout.Stack(upstreamMode, Check(nameof(vm.VerifyUpstreamHostname), "Verify upstream hostname"),
            Check(nameof(vm.VerifyUpstreamCertificateTrust), "Verify upstream certificate trust"),
            Check(nameof(vm.VerifyUpstreamCertificateValidity), "Verify upstream validity dates"),
            Input(nameof(vm.TlsServerName), "TLS server name / SNI override (blank = client SNI in Match mode, else host)"),
            new Expander
            {
                Header = "Upstream TLS options", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = ToolLayout.Stack(Input(nameof(vm.UpstreamRootPath), "Optional custom trusted CA · PEM/DER file", 1024),
                    Ui.AsyncButton("Browse trusted CA…", async () => { var path = await dialogs.OpenAsync(); if (path is not null) vm.UpstreamRootPath = path; }, ex => feedback.Text = ex.Message),
                    Ui.Text("Hostname, certificate trust and validity dates have separate checks, all enabled by default. Disabling trust permits an unknown CA/self-signed certificate; disabling validity permits expired/not-yet-valid certificates. Other TLS certificate checks still apply. Revocation is not checked. An explicit SNI override takes priority; otherwise Match incoming uses client SNI when available, then the upstream host."))
            });
        var configuration = ToolLayout.Columns(300,
            ToolLayout.Stack(
                ToolLayout.Columns(140, Input(nameof(vm.ListenAddress), "Listen IP"), Input(nameof(vm.ListenPort), "Listen port", 5)), incoming),
            ToolLayout.Stack(
                ToolLayout.Columns(140, Input(nameof(vm.UpstreamHost), "Upstream host / IP"), Input(nameof(vm.UpstreamPort), "Upstream port", 5)),
                upstream));
        var configHost = new ContentControl { Content = configuration, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        configHost.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var start = new Button { Content = "Start relay" };
        start.Click += async (_, _) =>
        {
            foreach (var tip in validationTips.Values) tip.IsOpen = false;
            var errors = vm.ValidateConfiguration();
            if (errors.Count > 0)
            {
                revealConfiguration();
                if (errors.ContainsKey(nameof(vm.CertificatePath)) || errors.ContainsKey(nameof(vm.CertificateName))) certificateOptions.IsExpanded = true;
                feedback.Text = string.Join(" ", errors.Values);
                // Wait for expanded panels to lay out before positioning field popups.
                start.DispatcherQueue.TryEnqueue(() =>
                {
                    var firstField = fields[errors.Keys.First()];
                    firstField.Focus(FocusState.Programmatic);
                    firstField.StartBringIntoView();
                    foreach (var (property, message) in errors)
                    {
                        if (!validationTips.TryGetValue(property, out var tip))
                        {
                            tip = new ToolTip { PlacementTarget = fields[property] };
                            validationTips[property] = tip;
                            ToolTipService.SetToolTip(fields[property], tip);
                        }
                        tip.Content = message; tip.IsOpen = true;
                    }
                });
                return;
            }
            feedback.Text = "";
            try { await vm.StartAsync(); } catch (Exception ex) { feedback.Text = ex.Message; }
        };
        start.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.CanConfigure)) });
        var stop = new Button { Content = "Stop relay" };
        stop.Click += async (_, _) => { try { await vm.StopAsync(); } catch (Exception ex) { feedback.Text = ex.Message; } };
        stop.SetBinding(Control.IsEnabledProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.IsRunning)) });
        var entries = new ListView
        {
            ItemsSource = vm.Entries, Header = Ui.Text("Traffic", 16), MinHeight = 0, SelectionMode = ListViewSelectionMode.Single,
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
            var viewer = new RawTextViewer { Header = header, MinHeight = 0, MaxHeight = double.PositiveInfinity, VerticalAlignment = VerticalAlignment.Stretch, TextWrapping = TextWrapping.Wrap };
            viewer.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
            return viewer;
        }
        Grid InspectPane(string property, string header, string copyLabel, Func<string> value)
        {
            var pane = new Grid { RowSpacing = 6 };
            pane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            pane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pane.Children.Add(Result(property, header));
            var copy = Ui.Button(copyLabel, () => Copy(value())); Grid.SetRow(copy, 1); pane.Children.Add(copy);
            return pane;
        }
        var dataTabs = new TabView { IsAddTabButtonVisible = false, CanDragTabs = false, CanReorderTabs = false, TabWidthMode = TabViewWidthMode.SizeToContent, VerticalAlignment = VerticalAlignment.Stretch };
        dataTabs.TabItems.Add(new TabViewItem { Header = "Hex", IsClosable = false,
            Content = InspectPane(nameof(vm.SelectedHex), "Exact received bytes (hex)", "Copy hex", () => vm.SelectedHex) });
        var textPane = new Grid { RowSpacing = 6 };
        textPane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        textPane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var encoding = new ComboBox { Header = "Text encoding", ItemsSource = TextEncodings.Options, HorizontalAlignment = HorizontalAlignment.Stretch };
        encoding.SetBinding(ComboBox.SelectedItemProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.TextEncoding)), Mode = BindingMode.TwoWay });
        textPane.Children.Add(encoding);
        var textPreview = InspectPane(nameof(vm.SelectedText), "Text preview (invalid/split characters replaced)", "Copy text", () => vm.SelectedText);
        Grid.SetRow(textPreview, 1); textPane.Children.Add(textPreview);
        dataTabs.TabItems.Add(new TabViewItem { Header = "Text", IsClosable = false, Content = textPane });
        dataTabs.TabItems.Add(new TabViewItem { Header = "Details", IsClosable = false,
            Content = InspectPane(nameof(vm.SelectedInfo), "Connection / capture details", "Copy details", () => vm.SelectedInfo) });
        dataTabs.SelectedIndex = 0;
        static ContentPresenter? FindTabContent(DependencyObject parent)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is ContentPresenter { Name: "TabContentPresenter" } presenter) return presenter;
                if (FindTabContent(child) is { } nested) return nested;
            }
            return null;
        }
        dataTabs.Loaded += (_, _) =>
        {
            if (FindTabContent(dataTabs) is { } presenter)
            {
                presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
                presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            }
        };
        var help = Ui.Text("Point the client at the listen IP/port; configure the real server as upstream. Incoming Auto mode peeks without consuming the first byte: 0x16 selects TLS, any other byte selects plain TCP. Auto waits for the client to send first; use Plain TCP for server-first protocols or plain data starting with 0x16. Auto creates a test certificate if none is selected. Export the public certificate and configure client trust and a matching DNS name/IP. Upstream connection can be Plain TCP, TLS or Match incoming. Match incoming mirrors each client's transport and reuses its SNI when available. An explicit SNI override takes priority; otherwise the upstream host is the fallback. The destination host/port remains configured, and each TLS leg negotiates its own version and cipher. Hostname, certificate trust and validity-date checks default on and can be toggled independently. Trust off permits unknown-CA/self-signed chains; validity dates off permits expired/not-yet-valid certificates. Other certificate and TLS checks remain enabled. Connection events record each verification setting. A custom CA applies only to this relay; revocation is not checked. TLS 1.2/1.3 is supported; legacy SSL, STARTTLS, UDP, HTTP CONNECT and mutual TLS are not supported. At most 16 clients and a 30-second setup/first-byte deadline. TCP read chunks are not protocol messages. Hex preserves exact received bytes; text uses the encoding selected on the Text tab (UTF-8 by default). Use Auto scroll to follow new traffic. Retention is 1,024 entries / 4 MiB; eviction does not interrupt forwarding. Export saves retained entries. Closing the tab stops its listener/connections. Windows TLS uses temporary key containers managed by .NET; no trust-store or firewall changes are made. Settings, passwords and traffic are not saved automatically.");
        var helpButton = new Button { Content = "Help" };
        helpButton.Flyout = new Flyout { Content = new ScrollViewer { Content = help, Width = 420, MaxHeight = 360, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        var toolbar = new AdaptiveColumns { MaximumColumns = 4, MinimumColumnWidth = 85 };
        toolbar.Children.Add(start); toolbar.Children.Add(stop);
        var exportTestCertificate = Ui.AsyncButton("Test cert…", async () =>
        {
            var path = await dialogs.SaveAsync("protobench-test-certificate", ".pem", "PEM certificate");
            if (path is not null) await vm.ExportTestCertificateAsync(path);
        }, ex => feedback.Text = ex.Message);
        ToolTipService.SetToolTip(exportTestCertificate, "Export public test certificate");
        toolbar.Children.Add(exportTestCertificate);
        toolbar.Children.Add(helpButton);
        var configurationScroll = new ScrollViewer
        {
            Content = configHost, MaxHeight = 180, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var connectionPanel = new Expander
        {
            Header = "Connection details", Content = configurationScroll, IsExpanded = true,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        revealConfiguration = () => connectionPanel.IsExpanded = true;
        var page = new Grid { Margin = new Thickness(12), RowSpacing = 8 };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var title = Ui.Text(vm.Title, 20); title.MaxWidth = 160; title.VerticalAlignment = VerticalAlignment.Center;
        heading.Children.Add(title); Grid.SetColumn(toolbar, 1); heading.Children.Add(toolbar);
        page.Children.Add(heading);
        Grid.SetRow(connectionPanel, 1); page.Children.Add(connectionPanel);
        page.SizeChanged += (_, _) =>
        {
            configurationScroll.MaxHeight = Math.Max(50, page.ActualHeight * .28);
            if (page.ActualHeight < 540) connectionPanel.IsExpanded = false;
        };
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsRunning) && vm.IsRunning) connectionPanel.IsExpanded = false; };

        var traffic = new Grid { RowSpacing = 6 };
        traffic.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        traffic.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var summary = Ui.BoundText(vm, nameof(vm.Summary)); summary.MaxLines = 4; summary.TextTrimming = TextTrimming.CharacterEllipsis;
        var capturePane = new Grid { RowSpacing = 6 };
        capturePane.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        capturePane.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        capturePane.Children.Add(ToolLayout.Stack(summary, Check(nameof(vm.AutoScroll), "Auto scroll")));
        Grid.SetRow(entries, 1); capturePane.Children.Add(entries);
        var panes = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        panes.Children.Add(capturePane); panes.Children.Add(dataTabs);
        bool? beside = null;
        void ReflowPanes()
        {
            var wide = panes.ActualWidth >= 560;
            if (beside == wide) return;
            beside = wide; panes.ColumnDefinitions.Clear(); panes.RowDefinitions.Clear();
            panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            if (wide) panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            else panes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(dataTabs, wide ? 1 : 0); Grid.SetRow(dataTabs, wide ? 0 : 1);
        }
        panes.SizeChanged += (_, _) => ReflowPanes();
        traffic.Children.Add(panes);
        var status = Ui.BoundText(vm, nameof(vm.Status)); status.MaxLines = 1; status.TextTrimming = TextTrimming.CharacterEllipsis;
        var captureActions = ToolLayout.Columns(160, status, Ui.Row(Ui.Button("Clear capture", vm.ClearCapture), Ui.AsyncButton("Export capture…", async () =>
        {
            var path = await dialogs.SaveAsync("tcp-relay-capture", ".jsonl", "JSON Lines capture");
            if (path is not null) await vm.ExportCaptureAsync(path);
        }, ex => feedback.Text = ex.Message)));
        Grid.SetRow(captureActions, 1); traffic.Children.Add(captureActions);
        var trafficFrame = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Background="{ThemeResource LayerFillColorDefaultBrush}" BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="8" Padding="10" />
            """);
        trafficFrame.Child = traffic; Grid.SetRow(trafficFrame, 2); page.Children.Add(trafficFrame);
        var error = Ui.BoundText(vm, nameof(vm.Error)); error.MaxLines = 2; error.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTipService.SetToolTip(error, Ui.BoundText(vm, nameof(vm.Error)));
        var messages = ToolLayout.Stack(error, feedback); Grid.SetRow(messages, 3); page.Children.Add(messages);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void ScrollToLatest()
        {
            if (vm.AutoScroll && vm.Entries.Count > 0) entries.ScrollIntoView(vm.Entries[^1]);
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.AutoScroll)) ScrollToLatest(); };
        timer.Tick += (_, _) =>
        {
            var previous = vm.Entries.LastOrDefault()?.Sequence;
            vm.RefreshCapture();
            if (vm.Entries.LastOrDefault()?.Sequence != previous) ScrollToLatest();
        };
        page.Loaded += (_, _) =>
        {
            // TabView's content presenter defaults to top alignment. Bounded star
            // rows need the full content viewport, rather than the desired height.
            for (DependencyObject? parent = VisualTreeHelper.GetParent(page); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is not ContentPresenter { Name: "TabContentPresenter" } presenter) continue;
                presenter.VerticalContentAlignment = VerticalAlignment.Stretch;
                presenter.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                break;
            }
            timer.Start();
        };
        page.Unloaded += (_, _) => { timer.Stop(); foreach (var tip in validationTips.Values) tip.IsOpen = false; };
        return page;
    }
}
