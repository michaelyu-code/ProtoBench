using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using Microsoft.Extensions.Logging;
using ProtoBench.Application.Tools;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Desktop.Features.Parsers;
using ProtoBench.Infrastructure.Updates;
using ProtoBench.Infrastructure.Plugins;

namespace ProtoBench.Desktop.Shell;

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
        CompactModeThresholdWidth = 640,
        ExpandedModeThresholdWidth = 1120,
        IsSettingsVisible = true,
        AlwaysShowHeader = false,
        IsPaneToggleButtonVisible = false
    };
    // TabView registers Ctrl+F4 on the entire control, including its workspace content.
    // Hide the automatic accelerator tooltip while retaining the tab shortcuts.
    private readonly TabView tabs = new() { IsAddTabButtonVisible = true, TabWidthMode = TabViewWidthMode.Equal,
        VerticalAlignment = VerticalAlignment.Stretch, KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden };
    private readonly InfoBar notification = new() { IsClosable = true, Severity = InfoBarSeverity.Error };
    private readonly InfoBar updateNotification = new() { IsClosable = true, Severity = InfoBarSeverity.Informational, Title = "Update available" };
    private Task<UpdateRelease?>? startupUpdateCheck;
    private UpdateRelease? availableUpdate;
    private bool hasUpdateCheckResult;
    private Action<UpdateRelease?>? updatePanelResult;
    private FrameworkElement? updatePanel;
    private bool startupCheckStarted, windowClosed;
    private bool? manualPaneOpen;
    private readonly Grid root = new();
    private readonly DashboardView dashboard;
    private readonly TabViewItem homeTab;
    private TabViewItem? settingsTab;
    private TabViewItem? pickerTab;
    private readonly ShellHeader header = new();
    private readonly NavigationViewItem home = new() { Content = "Dashboard", Icon = new SymbolIcon(Symbol.Home), Tag = "dashboard" };
    private readonly Dictionary<string, NavigationViewItem> toolItems = new();
    private readonly Dictionary<ToolCategory, NavigationViewItem> categoryItems = new();
    private readonly Dictionary<ToolCategory, NavigationViewItem> compactCategoryItems = new();
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
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "ProtoBench.ico"));
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 900));
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(header);
        Grid.SetRow(notification, 1);
        root.Children.Add(notification);
        Grid.SetRow(updateNotification, 2);
        root.Children.Add(updateNotification);
        var viewUpdate = new Button { Content = "View update" };
        viewUpdate.Click += (_, _) => ShowUpdateSettings();
        updateNotification.ActionButton = viewUpdate;
        Grid.SetRow(navigation, 3);
        root.Children.Add(navigation);
        Content = root;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(header.Bar);
        header.Bar.PaneToggleRequested += (_, _) =>
        {
            manualPaneOpen = !navigation.IsPaneOpen;
            ApplyNavigationPreference();
        };
        root.ActualThemeChanged += (_, _) => UpdateTitleBarColors();
        root.Loaded += (_, _) =>
        {
            UpdateTitleBarColors();
        };
        Closed += (_, _) => windowClosed = true;
        UpdateTitleBarColors();

        dashboard = new DashboardView(vm, (id, createNew) => Open(id, createNew), OpenRecentAsync, Report);
        homeTab = new TabViewItem { Header = "Dashboard", IsClosable = false, Content = dashboard, Tag = "dashboard" };
        dashboard.CategoryChanged += (_, _) => { if (ReferenceEquals(tabs.SelectedItem, homeTab)) SyncNavigation(); };
        tabs.TabItems.Add(homeTab);
        var openTabs = new Button { Content = "⌄", VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(openTabs, "Open tabs");
        ToolTipService.SetToolTip(openTabs, "Open tabs");
        var tabMenu = new MenuFlyout();
        tabMenu.Opening += (_, _) =>
        {
            tabMenu.Items.Clear();
            foreach (var item in tabs.TabItems.OfType<TabViewItem>())
            {
                var entry = new MenuFlyoutItem { Text = item.Header?.ToString() ?? "Workspace" };
                entry.Click += (_, _) => tabs.SelectedItem = item;
                tabMenu.Items.Add(entry);
            }
        };
        openTabs.Flyout = tabMenu;
        tabs.TabStripFooter = openTabs;
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
                Icon = new FontIcon { Glyph = NavigationIcons.Category(category.Key) },
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
            categoryItems.Add(category.Key, group);
            var compact = new NavigationViewItem
            {
                Content = group.Content, Tag = category.Key,
                Icon = new FontIcon { Glyph = NavigationIcons.Category(category.Key) }
            };
            ToolTipService.SetToolTip(compact, $"{category.Key.DisplayName()} · {category.Count()} tools");
            compactCategoryItems.Add(category.Key, compact);
            navigation.MenuItems.Add(group);
        }
        navigation.RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, (_, _) =>
        {
            // Let NavigationView finish its pane transition before replacing menu
            // containers; changing the collection during opening can dismiss it.
            navigation.DispatcherQueue.TryEnqueue(UpdateNavigationHierarchy);
        });
        navigation.SizeChanged += (_, _) => ApplyNavigationPreference();
        UpdateNavigationHierarchy();
        navigation.ItemInvoked += (_, args) =>
        {
            if (args.IsSettingsInvoked)
                ShowSettings();
            else if (args.InvokedItemContainer?.Tag is ToolCategory category)
            {
                tabs.SelectedItem = homeTab;
                dashboard.ShowCategory(category);
                SyncNavigation();
            }
            else if (args.InvokedItemContainer?.Tag is string id)
            {
                if (id == "dashboard")
                {
                    tabs.SelectedItem = homeTab;
                    dashboard.ShowCategory(null);
                    SyncNavigation();
                }
                else
                    Open(id);
            }
            if (manualPaneOpen != true && navigation.DisplayMode != NavigationViewDisplayMode.Expanded)
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
            {
                settingsTab = null;
                updatePanelResult = null;
                updatePanel = null;
            }
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

    private async Task CheckForUpdatesAtStartupAsync()
    {
        if (startupCheckStarted || !updates.Enabled) return;
        startupCheckStarted = true;
        try
        {
            startupUpdateCheck = updates.CheckOnStartupAsync();
            var release = await startupUpdateCheck;
            availableUpdate = release;
            hasUpdateCheckResult = true;
            if (windowClosed) return;
            updatePanelResult?.Invoke(release);
            if (release is null) return;
            updateNotification.Message = $"ProtoBench {release.Version} is available to download.";
            updateNotification.IsOpen = true;
        }
        catch (Exception ex)
        {
            if (!windowClosed) logger.LogWarning(new EventId(107), ex, "Startup update check failed");
        }
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
        // Load persisted network preferences before starting the one automatic update check.
        if (!windowClosed) await CheckForUpdatesAtStartupAsync();
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

    private void ApplyNavigationPreference()
    {
        if (manualPaneOpen is not { } open) return;
        // A manual open is docked, so overlay light-dismiss and responsive Auto
        // transitions cannot immediately undo the title-bar toggle.
        var mode = open ? NavigationViewPaneDisplayMode.Left
            : navigation.ActualWidth < navigation.CompactModeThresholdWidth
                ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.LeftCompact;
        if (navigation.PaneDisplayMode != mode) navigation.PaneDisplayMode = mode;
        if (navigation.IsPaneOpen != open) navigation.IsPaneOpen = open;
    }

    private void UpdateNavigationHierarchy()
    {
        // Compact category icons navigate to the dashboard directly. Child flyouts
        // otherwise consume the next category click as a light-dismiss action.
        var index = 1; // Dashboard remains the first entry.
        foreach (var (category, group) in categoryItems)
        {
            var item = navigation.IsPaneOpen ? group : compactCategoryItems[category];
            if (!ReferenceEquals(navigation.MenuItems[index], item)) navigation.MenuItems[index] = item;
            index++;
        }
        SyncNavigation();
    }

    private void SyncNavigation()
    {
        if (tabs.SelectedItem is not TabViewItem tab)
            return;
        if (tab.Tag is ToolWorkspace workspace && toolItems.TryGetValue(workspace.ToolId, out var item))
            navigation.SelectedItem = navigation.IsPaneOpen ? item : compactCategoryItems[vm.Tools.Get(workspace.ToolId).Category];
        else if (tab == homeTab && dashboard.SelectedCategory is { } category)
            navigation.SelectedItem = navigation.IsPaneOpen ? categoryItems[category] : compactCategoryItems[category];
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
                ToolTipService.SetToolTip(tab, tab.Header);
                tabs.TabItems.Add(tab);
                tabs.SelectedItem = tab;
                SyncNavigation();
                return workspace;
            }
            catch { vm.Workspaces.Close(workspace); throw; }
        }
        catch (Exception ex) { Report(ex); return null; }
    }

    private void ShowUpdateSettings()
    {
        ShowSettings();
        if (updatePanel is not { } panel) return;
        if (panel.IsLoaded) panel.StartBringIntoView();
        else
        {
            RoutedEventHandler? bringIntoView = null;
            bringIntoView = (_, _) =>
            {
                panel.Loaded -= bringIntoView;
                panel.StartBringIntoView();
            };
            panel.Loaded += bringIntoView;
        }
    }

    private void ShowSettings()
    {
        if (settingsTab is not null)
        {
            tabs.SelectedItem = settingsTab;
            return;
        }
        var theme = new ComboBox { Header = "Theme", ItemsSource = new[] { "Default", "Light", "Dark" }, SelectedItem = vm.Settings.Current.Theme, HorizontalAlignment = HorizontalAlignment.Stretch };
        theme.SelectionChanged += async (_, _) => { try { if (theme.SelectedItem is string selected) await vm.Settings.SetThemeAsync(selected); } catch (Exception ex) { Report(ex); } };
        var autoScroll = new CheckBox
        {
            Content = Ui.Text("Automatically scroll to results after parsing"),
            IsChecked = vm.Settings.Current.AutoScrollParserResults,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
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
            Content = Ui.Text("Auto-fit log parser results to the window"),
            IsChecked = vm.Settings.Current.AutoFitParserResults,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
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
            Content = Ui.Text("Verbose checks — show what was checked for each field"),
            IsChecked = vm.Settings.Current.VerboseParserChecks,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        verboseChecks.Click += async (_, _) =>
        {
            verboseChecks.IsEnabled = false;
            try { await vm.Settings.SetVerboseParserChecksAsync(verboseChecks.IsChecked == true); }
            catch (Exception ex) { verboseChecks.IsChecked = vm.Settings.Current.VerboseParserChecks; Report(ex); }
            finally { verboseChecks.IsEnabled = true; }
        };
        var localFiles = ToolLayout.Section("Local files and definitions", Ui.Text(ProtoBench.Infrastructure.Configuration.AppPaths.UserDirectory),
            Ui.AsyncButton("Open log folder", async () =>
            {
                var directory = ProtoBench.Infrastructure.Configuration.AppPaths.LogDirectory;
                Directory.CreateDirectory(directory);
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory);
                if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
                    throw new InvalidOperationException("The log folder could not be opened.");
            }, Report),
            Ui.AsyncButton("Open schema JSON folder", async () =>
            {
                var directory = Path.Combine(ProtoBench.Infrastructure.Configuration.AppPaths.UserDirectory, "definitions");
                Directory.CreateDirectory(Path.Combine(directory, "parsers"));
                Directory.CreateDirectory(Path.Combine(directory, "generators"));
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(directory);
                if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
                    throw new InvalidOperationException("The schema JSON folder could not be opened.");
            }, Report),
            new Expander { Header = "Definitions and logging details", HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Content = ToolLayout.Stack(
                    Ui.Text("Put schema-version 1 JSON files in definitions/parsers or definitions/generators under this directory, then restart. Give customized definitions unique IDs. Bundled defaults remain separate."),
                    Ui.Text("Log Parser streams logs into a temporary disk index. Search covers the full log. Oversized records produce diagnostics; legacy parser plugins need the streaming API for inputs above 16 MiB."),
                    Ui.Text("Local error logs include exception types, error codes and method stack traces. Exception messages, record contents and file paths are excluded.")) },
            Ui.Text("Reload the app to apply plugin and custom definition changes."),
            Ui.AsyncButton("Reload app", ReloadAppAsync, Report));
        var removeLocalData = ToolLayout.Section("Remove local data",
            Ui.Text("Permanently remove local settings, favorites, recent-file history, plugins, custom definitions and logs. ProtoBench will close and stay installed. Saved files outside the local data folder are kept."),
            Ui.AsyncButton("Remove all local data…", RemoveLocalDataAsync, Report));
        var sections = ToolLayout.Columns(420,
            ToolLayout.Stack(
                ToolLayout.Section("Appearance", theme),
                ToolLayout.Section("Log Parser", autoScroll, autoFit, verboseChecks),
                localFiles),
            ToolLayout.Stack(CreatePluginPanel(), CreateUpdatePanel(), removeLocalData));
        // Page scrolls its first child. Keep every settings section inside that one
        // adaptive surface so short windows can reach the bottom of the page.
        var settingsPage = ToolLayout.Page("Settings", "Preferences and local app management.", sections);
        settingsPage.MaxWidth = 1280;
        settingsTab = new TabViewItem
        {
            Header = "Settings",
            Tag = "settings",
            IsClosable = true,
            Content = settingsPage
        };
        tabs.TabItems.Add(settingsTab);
        tabs.SelectedItem = settingsTab;
    }

    private async Task RemoveLocalDataAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root.XamlRoot, Title = "Remove all ProtoBench local data?",
            Content = "This permanently deletes settings, favorites, recent-file history, installed plugins, custom definitions and logs from:\n\n" +
                ProtoBench.Infrastructure.Configuration.AppPaths.UserDirectory +
                "\n\nProtoBench will close and running operations will stop. Unsaved workspace inputs will be lost. The app stays installed. Saved files outside this folder are kept. This cannot be undone.",
            PrimaryButtonText = "Remove data and close", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        LocalDataRemoval.StartAfterExit();
        Close();
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
                var state = active is null ? "Not loaded this session" : $"Loaded · {active.Tools.Count} tools · {active.Parsers.Count} parsers · {active.Generators.Count} generators · {active.FieldDecoders.Count} decoders · {active.TelnetServers.Count} Telnet profiles";
                if (item.PendingVersion is { } pendingVersion) state += $" · Update to {pendingVersion} ready — reload app to apply";
                var enabled = new CheckBox { Content = Ui.Text($"Enable {manifest.Name} ({manifest.Version})"), IsChecked = item.Enabled };
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
                var existing = pluginPackages.Installed().FirstOrDefault(p => p.Manifest?.Id == manifest.Id);
                var updating = existing is not null;
                var dialog = new ContentDialog
                {
                    XamlRoot = root.XamlRoot, Title = $"{(updating ? "Update" : "Import")} {manifest.Name}?",
                    Content = $"Version {manifest.Version} · SDK API {manifest.ApiVersion}\n\nPlugins run developer code with the app's permissions. Import only code you trust. " +
                        (updating ? $"This replaces version {existing!.PendingVersion ?? existing.Manifest!.Version} after you reload the app. Your enabled setting is kept." : "It will be enabled at the next app start."),
                    PrimaryButtonText = updating ? "Import update" : "Import plugin", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close
                };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
                await Task.Run(() => pluginPackages.Import(source));
                status.Text = updating ? "Plugin update ready. Use Reload app to apply it." : "Plugin imported. Use Reload app to load its tools and formats.";
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
        var panel = ToolLayout.Stack();
        var section = ToolLayout.Section("Updates", panel);
        section.Visibility = updates.Enabled ? Visibility.Visible : Visibility.Collapsed;
        updatePanel = section;
        if (!updates.Enabled) return section;
        panel.Children.Add(Ui.Text($"Current version: {updates.CurrentVersion}"));
        var status = Ui.Text("Check for a new release when you are ready.");
        var notes = Ui.Text("");
        var check = new Button { Content = "Check for updates" };
        var install = new Button { Content = "Download update", Visibility = Visibility.Collapsed };
        var ready = false;
        var proxyChanged = false;
        var proxyAddress = new TextBox
        {
            Header = "Fallback proxy address", PlaceholderText = "http://proxy.company:8080",
            Text = vm.Settings.Current.UpdateProxyAddress, MaxLength = 2048
        };
        var proxyStatus = Ui.Text("", 12);
        var saveProxy = Ui.AsyncButton("Save proxy", async () =>
        {
            check.IsEnabled = install.IsEnabled = false;
            try
            {
                await vm.Settings.SetUpdateProxyAsync(proxyAddress.Text);
                proxyAddress.Text = vm.Settings.Current.UpdateProxyAddress;
                proxyChanged = true;
                proxyStatus.Text = proxyAddress.Text.Length == 0 ? "Proxy fallback disabled." : "Proxy saved. It will be tried if the normal connection fails.";
            }
            finally { check.IsEnabled = install.IsEnabled = true; }
        }, ex => proxyStatus.Text = ex.Message);
        var downloadPage = Ui.AsyncButton("Open download page", async () =>
        {
            if (!await Windows.System.Launcher.LaunchUriAsync(updates.DownloadPage))
                throw new InvalidOperationException("The download page could not be opened.");
        }, ex => status.Text = ex.Message);
        void DisplayRelease(UpdateRelease? release)
        {
            availableUpdate = release;
            hasUpdateCheckResult = true;
            status.Text = release is null ? "You are up to date." : release.CanInstall
                ? $"Version {release.Version} is available."
                : $"Version {release.Version} is available. Open the download page to download and replace this portable copy.";
            notes.Text = release?.ReleaseNotes ?? "";
            install.Content = "Download update";
            install.Visibility = release?.CanInstall == true ? Visibility.Visible : Visibility.Collapsed;
            if (release is null) updateNotification.IsOpen = false;
        }
        if (hasUpdateCheckResult) DisplayRelease(availableUpdate);
        updatePanelResult = release => { if (check.IsEnabled && !ready) DisplayRelease(release); };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            proxyAddress.IsEnabled = saveProxy.IsEnabled = false;
            install.Visibility = Visibility.Collapsed;
            ready = false;
            hasUpdateCheckResult = false;
            notes.Text = "";
            status.Text = "Checking for updates…";
            try
            {
                // If the proxy was saved after startup began, let that request finish and then retry with the new setting.
                if (proxyChanged && startupUpdateCheck is { IsCompleted: false })
                    try { await startupUpdateCheck; } catch { }
                var release = await (startupUpdateCheck is { IsCompleted: false } ? startupUpdateCheck : updates.CheckAsync());
                DisplayRelease(release);
                proxyChanged = false;
            }
            catch { status.Text = "Could not check for updates. Try again or open the download page."; }
            finally { check.IsEnabled = proxyAddress.IsEnabled = saveProxy.IsEnabled = true; }
        };
        install.Click += async (_, _) =>
        {
            install.IsEnabled = check.IsEnabled = false;
            proxyAddress.IsEnabled = saveProxy.IsEnabled = false;
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
            finally { install.IsEnabled = check.IsEnabled = proxyAddress.IsEnabled = saveProxy.IsEnabled = true; }
        };
        panel.Children.Add(proxyAddress);
        panel.Children.Add(Ui.Text("Optional HTTP(S) proxy for update checks and downloads. The normal connection is tried first. Leave blank and save to disable.", 12));
        panel.Children.Add(saveProxy);
        panel.Children.Add(proxyStatus);
        panel.Children.Add(check);
        panel.Children.Add(status);
        panel.Children.Add(notes);
        panel.Children.Add(install);
        panel.Children.Add(downloadPage);
        return section;
    }
}
