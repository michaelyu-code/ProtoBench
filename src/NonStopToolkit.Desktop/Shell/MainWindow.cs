using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.Extensions.Logging;
using NonStopToolkit.Application.Tools;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Desktop.Controls;
using NonStopToolkit.Desktop.Services;
using NonStopToolkit.Desktop.Features.Parsers;
using NonStopToolkit.Infrastructure.Updates;
using NonStopToolkit.Infrastructure.Plugins;

namespace NonStopToolkit.Desktop.Shell;

public sealed class MainWindow : Window
{
    private readonly ShellViewModel vm;
    private readonly IReadOnlyDictionary<string, IWorkspaceViewFactory> views;
    private readonly ILogger<MainWindow> logger;
    private readonly UpdateService updates;
    private readonly PluginPackageManager pluginPackages;
    private readonly PluginCatalog pluginCatalog;
    private readonly FileDialogService files;
    private readonly NavigationView navigation = new()
    {
        IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
        PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
        OpenPaneLength = 220,
        CompactModeThresholdWidth = 880,
        ExpandedModeThresholdWidth = 880,
        IsSettingsVisible = true,
        AlwaysShowHeader = false,
        IsPaneToggleButtonVisible = false
    };
    private readonly TabView tabs = new() { IsAddTabButtonVisible = true, TabWidthMode = TabViewWidthMode.SizeToContent };
    private readonly InfoBar notification = new() { IsClosable = true, Severity = InfoBarSeverity.Error };
    private readonly Grid root = new();
    private readonly DashboardView dashboard;
    private readonly TabViewItem homeTab;
    private TabViewItem? settingsTab;
    private TabViewItem? pickerTab;
    private readonly ShellHeader header = new();
    private readonly NavigationViewItem home = new() { Content = "Dashboard", Icon = new SymbolIcon(Symbol.Home), Tag = "dashboard" };
    private readonly Dictionary<string, NavigationViewItem> toolItems = new();
    private readonly Dictionary<string, int> instanceNumbers = new();

    public MainWindow(ShellViewModel vm, IEnumerable<IWorkspaceViewFactory> views, ILogger<MainWindow> logger, UpdateService updates,
        PluginPackageManager pluginPackages, PluginCatalog pluginCatalog, FileDialogService files)
    {
        this.vm = vm;
        this.views = views.ToDictionary(v => v.ToolId);
        this.logger = logger;
        this.updates = updates;
        this.pluginPackages = pluginPackages;
        this.pluginCatalog = pluginCatalog;
        this.files = files;
        Title = "ProtoBench";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 900));
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(header);
        Grid.SetRow(notification, 1);
        root.Children.Add(notification);
        Grid.SetRow(navigation, 2);
        root.Children.Add(navigation);
        Content = root;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(header.Bar);
        header.Bar.PaneToggleRequested += (_, _) => navigation.IsPaneOpen = !navigation.IsPaneOpen;
        root.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        root.Loaded += (_, _) => UpdateTitleBarColors();
        UpdateTitleBarColors();

        dashboard = new DashboardView(vm, (id, createNew) => Open(id, createNew), OpenRecentAsync, Report);
        homeTab = new TabViewItem { Header = "Dashboard", IsClosable = false, Content = dashboard, Tag = "dashboard" };
        tabs.TabItems.Add(homeTab);
        tabs.SelectedItem = homeTab;
        navigation.Content = tabs;
        navigation.MenuItems.Add(home);
        // NavigationView owns its menu's vertical ScrollViewer. Settings stays in the footer.
        foreach (var category in vm.Tools.Tools.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            var group = new NavigationViewItem
            {
                Content = category.Key.DisplayName(),
                Tag = category.Key,
                Icon = new SymbolIcon(CategoryIcon(category.Key)),
                SelectsOnInvoked = true,
                IsExpanded = vm.Tools.Tools.Count <= 8
            };
            ToolTipService.SetToolTip(group, $"{category.Key.DisplayName()} · {category.Count()} tools");
            foreach (var tool in category.OrderBy(t => t.Name))
            {
                var item = new NavigationViewItem { Content = tool.Name, Tag = tool.Id, Icon = new FontIcon { Glyph = tool.Icon } };
                toolItems.Add(tool.Id, item);
                group.MenuItems.Add(item);
            }
            navigation.MenuItems.Add(group);
        }
        navigation.ItemInvoked += (_, args) =>
        {
            if (args.IsSettingsInvoked)
                ShowSettings();
            else if (args.InvokedItemContainer?.Tag is ToolCategory category)
            {
                dashboard.ShowCategory(category);
                tabs.SelectedItem = homeTab;
            }
            else if (args.InvokedItemContainer?.Tag is string id)
            {
                if (id == "dashboard")
                {
                    dashboard.ShowCategory(null);
                    tabs.SelectedItem = homeTab;
                }
                else
                    Open(id);
            }
            if (navigation.DisplayMode != NavigationViewDisplayMode.Expanded)
                navigation.IsPaneOpen = false;
        };
        tabs.AddTabButtonClick += (_, _) => ShowToolPicker();
        tabs.TabCloseRequested += (_, args) =>
        {
            if (args.Tab == homeTab)
                return;
            if (args.Tab.Tag is ToolWorkspace workspace)
                vm.Workspaces.Close(workspace);
            if (args.Tab == settingsTab)
                settingsTab = null;
            if (args.Tab == pickerTab)
                pickerTab = null;
            tabs.TabItems.Remove(args.Tab);
            if (tabs.SelectedItem is null)
                tabs.SelectedItem = homeTab;
        };
        tabs.SelectionChanged += (_, _) => SyncNavigation();
        vm.Settings.Changed += () => { ApplyTheme(); dashboard.Refresh(); };
        navigation.SelectedItem = home;
    }

    public async void InitializeAsync(IReadOnlyList<string> diagnostics)
    {
        try
        {
            await vm.Settings.LoadAsync();
        }
        catch (Exception ex) { Report(ex); }
        if (diagnostics.Count > 0)
        {
            notification.Message = string.Join(Environment.NewLine, diagnostics);
            notification.IsOpen = true;
            logger.LogWarning(new EventId(101), "Definition validation failed");
        }
    }

    private void Report(Exception ex)
    {
        logger.LogError(new EventId(100), ex, "Desktop action failed");
        notification.Message = ex.Message;
        notification.IsOpen = true;
    }

    private void ApplyTheme()
    {
        root.RequestedTheme = Enum.Parse<ElementTheme>(vm.Settings.Current.Theme);
        UpdateTitleBarColors();
    }

    private void UpdateTitleBarColors()
    {
        var dark = root.ActualTheme == ElementTheme.Dark;
        var background = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        var foreground = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
        header.Bar.Background = new SolidColorBrush(background);
        root.Background = new SolidColorBrush(background);
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        var caption = AppWindow.TitleBar;
        caption.BackgroundColor = background;
        caption.InactiveBackgroundColor = background;
        caption.ForegroundColor = foreground;
        caption.InactiveForegroundColor = dark ? Microsoft.UI.Colors.Silver : Microsoft.UI.Colors.DimGray;
        caption.ButtonBackgroundColor = background;
        caption.ButtonInactiveBackgroundColor = background;
        caption.ButtonForegroundColor = foreground;
        caption.ButtonInactiveForegroundColor = caption.InactiveForegroundColor;
        caption.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 48, 48, 48) : Windows.UI.Color.FromArgb(255, 225, 225, 225);
        caption.ButtonHoverForegroundColor = foreground;
        caption.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(255, 56, 56, 56) : Windows.UI.Color.FromArgb(255, 210, 210, 210);
        caption.ButtonPressedForegroundColor = foreground;
    }

    private void ShowToolPicker()
    {
        if (pickerTab is not null)
        {
            tabs.SelectedItem = pickerTab;
            ((ToolPickerView)pickerTab.Content).Reset();
            return;
        }
        var tab = new TabViewItem { Header = "New tab", Tag = "new-tab", IsClosable = true };
        tab.Content = new ToolPickerView(vm.Tools, id =>
        {
            var workspace = Open(id, true);
            if (workspace is null) return;
            var workspaceTab = tabs.TabItems.OfType<TabViewItem>().First(t => ReferenceEquals(t.Tag, workspace));
            tabs.TabItems.Remove(tab);
            pickerTab = null;
            // Removing a tab can move TabView's selection; keep the new workspace active.
            tabs.SelectedItem = workspaceTab;
            SyncNavigation();
        });
        pickerTab = tab;
        tabs.TabItems.Add(tab);
        tabs.SelectedItem = tab;
    }

    private void SyncNavigation()
    {
        if (tabs.SelectedItem is not TabViewItem tab)
            return;
        if (tab.Tag is ToolWorkspace workspace && toolItems.TryGetValue(workspace.ToolId, out var item))
            navigation.SelectedItem = item;
        else
            navigation.SelectedItem = tab == settingsTab ? navigation.SettingsItem : home;
    }

    private async Task OpenRecentAsync(string file)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException("The recent file no longer exists.");
        // A new workspace keeps previously inspected files and ongoing operations intact.
        if (Open("log-parser", true) is ParserWorkspace parser)
            await parser.OpenAsync(file);
    }

    private ToolWorkspace? Open(string id, bool createNew = false)
    {
        try
        {
            if (!createNew)
            {
                var existing = tabs.TabItems.OfType<TabViewItem>().LastOrDefault(t => t.Tag is ToolWorkspace w && w.ToolId == id);
                if (existing?.Tag is ToolWorkspace current)
                {
                    tabs.SelectedItem = existing;
                    SyncNavigation();
                    return current;
                }
            }
            var workspace = vm.Workspaces.Open(id);
            try
            {
                var content = views[id].Create(workspace);
                var number = instanceNumbers.GetValueOrDefault(id) + 1;
                instanceNumbers[id] = number;
                var tab = new TabViewItem
                {
                    Header = number == 1 ? workspace.Title : $"{workspace.Title} ({number})",
                    Tag = workspace,
                    Content = content,
                    IsClosable = true,
                    IconSource = new FontIconSource { Glyph = vm.Tools.Get(id).Icon }
                };
                tabs.TabItems.Add(tab);
                tabs.SelectedItem = tab;
                SyncNavigation();
                return workspace;
            }
            catch { vm.Workspaces.Close(workspace); throw; }
        }
        catch (Exception ex) { Report(ex); return null; }
    }

    private void ShowSettings()
    {
        if (settingsTab is not null)
        {
            tabs.SelectedItem = settingsTab;
            return;
        }
        var theme = new ComboBox { Header = "Appearance", ItemsSource = new[] { "Default", "Light", "Dark" }, SelectedItem = vm.Settings.Current.Theme, MinWidth = 240 };
        theme.SelectionChanged += async (_, _) => { try { if (theme.SelectedItem is string selected) await vm.Settings.SetThemeAsync(selected); } catch (Exception ex) { Report(ex); } };
        var autoScroll = new CheckBox
        {
            Content = "Automatically scroll to results after parsing",
            IsChecked = vm.Settings.Current.AutoScrollParserResults
        };
        autoScroll.Click += async (_, _) =>
        {
            autoScroll.IsEnabled = false;
            try { await vm.Settings.SetParserAutoScrollAsync(autoScroll.IsChecked == true); }
            catch (Exception ex) { autoScroll.IsChecked = vm.Settings.Current.AutoScrollParserResults; Report(ex); }
            finally { autoScroll.IsEnabled = true; }
        };
        var autoFit = new CheckBox
        {
            Content = "Auto-fit log parser results to the window",
            IsChecked = vm.Settings.Current.AutoFitParserResults
        };
        autoFit.Click += async (_, _) =>
        {
            autoFit.IsEnabled = false;
            try { await vm.Settings.SetParserAutoFitAsync(autoFit.IsChecked == true); }
            catch (Exception ex) { autoFit.IsChecked = vm.Settings.Current.AutoFitParserResults; Report(ex); }
            finally { autoFit.IsEnabled = true; }
        };
        var verboseChecks = new CheckBox
        {
            Content = "Verbose checks — show what was checked for each field",
            IsChecked = vm.Settings.Current.VerboseParserChecks
        };
        verboseChecks.Click += async (_, _) =>
        {
            verboseChecks.IsEnabled = false;
            try { await vm.Settings.SetVerboseParserChecksAsync(verboseChecks.IsChecked == true); }
            catch (Exception ex) { verboseChecks.IsChecked = vm.Settings.Current.VerboseParserChecks; Report(ex); }
            finally { verboseChecks.IsEnabled = true; }
        };
        settingsTab = new TabViewItem
        {
            Header = "Settings",
            Tag = "settings",
            IsClosable = true,
            Content = new ScrollViewer
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = Ui.Column(Ui.Heading("Settings"), theme,
                    ToolLayout.Section("Log Parser", autoScroll, autoFit, verboseChecks),
                    Ui.Text("Reload the app to apply plugin and custom definition changes."),
                    Ui.AsyncButton("Reload app", ReloadAppAsync, Report),
                    Ui.Text("Local settings, custom definitions and logs"), Ui.Text(NonStopToolkit.Infrastructure.Configuration.AppPaths.UserDirectory),
                    Ui.AsyncButton("Open log folder", async () =>
                    {
                        var directory = NonStopToolkit.Infrastructure.Configuration.AppPaths.LogDirectory;
                        Directory.CreateDirectory(directory);
                        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory);
                        if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
                            throw new InvalidOperationException("The log folder could not be opened.");
                    }, Report),
                    Ui.AsyncButton("Open schema JSON folder", async () =>
                    {
                        var directory = Path.Combine(NonStopToolkit.Infrastructure.Configuration.AppPaths.UserDirectory, "definitions");
                        Directory.CreateDirectory(Path.Combine(directory, "parsers"));
                        Directory.CreateDirectory(Path.Combine(directory, "generators"));
                        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory);
                        if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
                            throw new InvalidOperationException("The schema JSON folder could not be opened.");
                    }, Report),
                    Ui.Text("Put schema-version 1 JSON files in definitions/parsers or definitions/generators under this directory, then restart. Give customized definitions unique IDs. Bundled defaults remain separate."),
                    Ui.Text("Log Parser streams files and clipboard text into a temporary disk index. Search covers the full indexed log, with 2,000 messages per page by default. Individual oversized records are skipped with diagnostics; regex execution is limited to 100 ms per operation. Legacy parser plugins need the streaming API for inputs above 16 MiB."),
                    Ui.Text("Local error logs include exception types, error codes and method stack traces. Exception messages, record contents and file paths are excluded."),
                    CreatePluginPanel(), CreateUpdatePanel())
            }
        };
        tabs.TabItems.Add(settingsTab);
        tabs.SelectedItem = settingsTab;
    }

    private async Task ReloadAppAsync()
    {
        if (vm.Workspaces.Workspaces.Count > 0)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = root.XamlRoot, Title = "Reload app?",
                Content = "Open workspaces and their inputs will be closed, and running operations will stop. Save or copy anything you need before continuing.",
                PrimaryButtonText = "Reload app", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        // A successful restart terminates this process and launches a fresh instance.
        // If it returns, keep the current app open and report the failure.
        var reason = Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
        throw new InvalidOperationException(reason switch
        {
            Windows.ApplicationModel.Core.AppRestartFailureReason.RestartPending => "An app reload is already pending. Please wait.",
            Windows.ApplicationModel.Core.AppRestartFailureReason.InvalidUser => "The app could not reload because the current Windows user is unavailable.",
            _ => "The app could not reload. Your current session is still open. Try again or close and reopen the app."
        });
    }

    private UIElement CreatePluginPanel()
    {
        var installed = ToolLayout.Stack();
        var status = Ui.Text("");
        void Refresh()
        {
            installed.Children.Clear();
            foreach (var item in pluginPackages.Installed())
            {
                if (item.Manifest is not { } manifest)
                {
                    installed.Children.Add(Ui.Text($"{Path.GetFileName(item.Directory)} · {item.Error}"));
                    continue;
                }
                var active = pluginCatalog.Plugins.FirstOrDefault(p => p.Manifest.Id == manifest.Id);
                var state = active is null ? "Not loaded this session" : $"Loaded · {active.Tools.Count} tools · {active.Parsers.Count} parsers · {active.Generators.Count} generators · {active.FieldDecoders.Count} decoders";
                var enabled = new CheckBox { Content = $"Enable {manifest.Name} ({manifest.Version})", IsChecked = item.Enabled };
                enabled.Click += (_, _) =>
                {
                    try { pluginPackages.SetEnabled(manifest.Id, enabled.IsChecked == true); status.Text = "Plugin setting saved. Restart the app to apply it."; }
                    catch (Exception ex) { enabled.IsChecked = item.Enabled; Report(ex); }
                };
                installed.Children.Add(ToolLayout.Stack(enabled, Ui.Text(state, 12)));
            }
            if (installed.Children.Count == 0) installed.Children.Add(Ui.Text("No plugins installed. Import a developer's package to add custom tools or formats."));
        }
        Refresh();
        return ToolLayout.Section("Plugins & custom tools", Ui.Text("Import a .nstplugin/.zip package, plugin.json, or a DLL beside its plugin.json. Changes take effect after restarting the app."),
            Ui.AsyncButton("Import plugin…", async () =>
            {
                var source = await files.OpenAsync();
                if (source is null) return;
                var manifest = pluginPackages.Inspect(source);
                var dialog = new ContentDialog
                {
                    XamlRoot = root.XamlRoot, Title = $"Import {manifest.Name}?",
                    Content = $"Version {manifest.Version} · SDK API {manifest.ApiVersion}\n\nPlugins run developer code with the app's permissions. Import only code you trust. It will be enabled at the next app start.",
                    PrimaryButtonText = "Import plugin", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                await Task.Run(() => pluginPackages.Import(source));
                status.Text = "Plugin imported. Restart the app to load its tools and formats.";
                Refresh();
            }, Report),
            Ui.AsyncButton("Open plugins folder", async () =>
            {
                Directory.CreateDirectory(pluginPackages.Root);
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(pluginPackages.Root);
                if (!await Windows.System.Launcher.LaunchFolderAsync(folder)) throw new IOException("Could not open plugins folder.");
            }, Report), status, installed);
    }

    private UIElement CreateUpdatePanel()
    {
        var panel = new StackPanel { Spacing = 12, Visibility = updates.Enabled ? Visibility.Visible : Visibility.Collapsed };
        if (!updates.Enabled) return panel;
        panel.Children.Add(Ui.Heading("Updates"));
        panel.Children.Add(Ui.Text($"Current version: {updates.CurrentVersion}"));
        var status = Ui.Text("Check for a new release when you are ready.");
        var notes = Ui.Text("");
        var check = new Button { Content = "Check for updates" };
        var install = new Button { Content = "Download update", Visibility = Visibility.Collapsed };
        var ready = false;
        var downloadPage = Ui.AsyncButton("Open download page", async () =>
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(updates.DownloadPage))
                throw new InvalidOperationException("The download page could not be opened.");
        }, ex => status.Text = ex.Message);
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            install.Visibility = Visibility.Collapsed;
            ready = false;
            notes.Text = "";
            status.Text = "Checking for updates…";
            try
            {
                var release = await updates.CheckAsync();
                status.Text = release is null ? "You are up to date." : $"Version {release.Version} is available.";
                notes.Text = release?.ReleaseNotes ?? "";
                install.Content = "Download update";
                install.Visibility = release?.CanInstall == true ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { status.Text = "Could not check for updates. Try again or open the download page."; }
            finally { check.IsEnabled = true; }
        };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = check.IsEnabled = false;
            try
            {
                if (!ready)
                {
                    status.Text = "Downloading update…";
                    await updates.DownloadAsync();
                    ready = true;
                    install.Content = "Restart to update";
                    status.Text = "Update downloaded. Restart when you are ready.";
                }
                else
                {
                    var dialog = new ContentDialog
                    {
                        XamlRoot = root.XamlRoot, Title = "Restart to update?",
                        Content = "Open workspaces and their inputs will be closed. Save or copy anything you need before continuing.",
                        PrimaryButtonText = "Restart to update", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
                    };
                    if (await dialog.ShowAsync() == ContentDialogResult.Primary) updates.Restart();
                }
            }
            catch { status.Text = "Could not install the update. Try again or open the download page."; }
            finally { install.IsEnabled = check.IsEnabled = true; }
        };
        panel.Children.Add(check);
        panel.Children.Add(status);
        panel.Children.Add(notes);
        panel.Children.Add(install);
        panel.Children.Add(downloadPage);
        return panel;
    }

    private static Symbol CategoryIcon(ToolCategory category) => category switch
    {
        ToolCategory.Parsers => Symbol.Document,
        ToolCategory.Converters => Symbol.Calculator,
        ToolCategory.Calculators => Symbol.Calculator,
        ToolCategory.Generators => Symbol.Save,
        ToolCategory.Comparisons => Symbol.Switch,
        _ => Symbol.Repair
    };
}


