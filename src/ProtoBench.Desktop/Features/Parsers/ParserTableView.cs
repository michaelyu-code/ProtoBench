using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using ProtoBench.Infrastructure.Logs;

namespace ProtoBench.Desktop.Features.Parsers;

/// <summary>A vertically virtualized table over the current query page. Raw values come from the index.</summary>
public sealed class ParserTableView : UserControl
{
    public sealed class TableColumn(LogTableColumn definition, double initialWidth) : ObservableObject
    {
        public LogTableColumn Definition { get; } = definition;
        public double InitialWidth { get; private set; } = initialWidth;
        public bool HasManualWidth { get; private set; }
        private double width = initialWidth;
        public double Width { get => width; set { HasManualWidth = true; SetProperty(ref width, Math.Clamp(value, 80, 1200)); } }
        public void Fit(double value)
        {
            InitialWidth = Math.Clamp(Math.Ceiling(value), 80, 1200);
            if (!HasManualWidth) SetProperty(ref width, InitialWidth, nameof(Width));
        }
        public void ResetWidth() { HasManualWidth = false; SetProperty(ref width, InitialWidth, nameof(Width)); }
    }
    public sealed class TableLayout : ObservableObject
    {
        private double horizontalOffset, frozenWidth;
        public double HorizontalOffset { get => horizontalOffset; set => SetProperty(ref horizontalOffset, value); }
        public double FrozenWidth
        {
            get => frozenWidth;
            set { if (SetProperty(ref frozenWidth, value)) { OnPropertyChanged(nameof(ScrollingMargin)); OnPropertyChanged(nameof(FrozenVisibility)); } }
        }
        public Thickness ScrollingMargin => new(FrozenWidth, 0, 0, 0);
        public Visibility FrozenVisibility => FrozenWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    public sealed record TableCell(string Text, Brush Fill, Brush CheckBrush, Visibility MarkerVisibility, string StatusText);
    public sealed record TableRow(long Id, TableCell[] Cells, TableColumn[] Columns, Brush Background, Brush Accent, TableLayout Layout)
    {
        public override string ToString() => string.Join(" · ", Cells.Take(4).Select(v => v.Text.Length > 120 ? v.Text[..120] + "…" : v.Text));
    }

    private readonly ParserWorkspace workspace;
    private readonly Button columnsButton = new() { Content = "Columns" };
    private readonly Button openButton = new() { Content = "Open message details", IsEnabled = false };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Grid header = new() { Margin = new Thickness(4, 0, 16, 0) };
    private readonly StackPanel frozenHeader = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel scrollingHeader = new() { Orientation = Orientation.Horizontal };
    private readonly Border frozenHeaderPanel = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TranslateTransform frozenHeaderTransform = new();
    private readonly TableLayout layout = new();
    private readonly ListView data = new()
    {
        SelectionMode = ListViewSelectionMode.Single, HorizontalContentAlignment = HorizontalAlignment.Stretch
    };
    private readonly Grid canvas = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ScrollViewer viewport = new()
    {
        HorizontalScrollMode = ScrollMode.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
    };
    private readonly DispatcherTimer columnDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Dictionary<string, TableColumn> columnWidths = new(StringComparer.Ordinal);
    private readonly List<(LogTableColumn Column, CheckBox Box, ToggleButton Pin, Grid Row)> choices = [];
    private readonly HashSet<string> pinned = new(StringComparer.Ordinal);
    private readonly HashSet<string> pinOverrides = new(StringComparer.Ordinal);
    private readonly ParserTablePan pan = new();
    private readonly DispatcherTimer panTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private ScrollViewer? dataScroller;
    private IReadOnlyList<LogTableColumn> catalog = [];
    private LogPage? page;
    private CancellationTokenSource? loading;
    private long version;
    private bool updatingChoices, active;
    private TableColumn[] visibleColumns = [];
    private int frozenCount;
    public event Action<long>? OpenMessage;

    public ParserTableView(ParserWorkspace workspace)
    {
        this.workspace = workspace;
        AutomationProperties.SetName(data, "Messages table");
        ScrollViewer.SetVerticalScrollMode(data, ScrollMode.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(data, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(data, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(data, ScrollBarVisibility.Disabled);
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32d));
        data.ItemContainerStyle = style;
        data.SelectionChanged += (_, _) => openButton.IsEnabled = data.SelectedItem is TableRow;
        data.DoubleTapped += (_, _) => OpenSelected();
        openButton.Click += (_, _) => OpenSelected();
        var request = new TextBlock { Text = "■ Request", Foreground = new SolidColorBrush(Colors.DodgerBlue), VerticalAlignment = VerticalAlignment.Center };
        var reply = new TextBlock { Text = "■ Reply / response", Foreground = new SolidColorBrush(Colors.MediumPurple), VerticalAlignment = VerticalAlignment.Center };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        toolbar.Children.Add(columnsButton); toolbar.Children.Add(openButton); toolbar.Children.Add(request); toolbar.Children.Add(reply);
        var root = new Grid { RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(toolbar); Grid.SetRow(status, 1); root.Children.Add(status);
        Grid.SetRow(viewport, 2); root.Children.Add(viewport);
        canvas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        canvas.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        frozenHeaderPanel.Child = frozenHeader;
        frozenHeaderPanel.RenderTransform = frozenHeaderTransform;
        header.Children.Add(scrollingHeader); header.Children.Add(frozenHeaderPanel);
        canvas.Children.Add(header); Grid.SetRow(data, 1); canvas.Children.Add(data);
        viewport.Content = canvas;
        viewport.SizeChanged += (_, e) => canvas.Height = Math.Max(80, e.NewSize.Height - 18);
        viewport.ViewChanged += (_, _) =>
        {
            layout.HorizontalOffset = viewport.HorizontalOffset;
            frozenHeaderTransform.X = layout.HorizontalOffset;
        };
        data.Loaded += (_, _) => dataScroller = FindScroller(data);
        Content = root;
        ConfigurePanning();
        columnDelay.Tick += async (_, _) => { columnDelay.Stop(); await ReloadAsync(); };
        Unloaded += (_, _) => Cancel();
        void UpdateLegendColors()
        {
            var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
            var text = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
            request.Foreground = new SolidColorBrush(highContrast ? text : ActualTheme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 0, 95, 184) : Colors.DodgerBlue);
            reply.Foreground = new SolidColorBrush(highContrast ? text : ActualTheme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 112, 48, 160) : Colors.MediumPurple);
        }
        UpdateLegendColors();
        ActualThemeChanged += async (_, _) => { UpdateLegendColors(); if (active) await ReloadAsync(); };
    }

    private void OpenSelected() { if (data.SelectedItem is TableRow row) OpenMessage?.Invoke(row.Id); }
    public void Cancel()
    {
        active = false; version++; columnDelay.Stop(); loading?.Cancel(); StopPan();
    }

    private static ScrollViewer? FindScroller(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is ScrollViewer scroller) return scroller;
            if (FindScroller(child) is { } found) return found;
        }
        return null;
    }
    private void ConfigurePanning()
    {
        IsTabStop = true;
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(viewport);
            if (pan.Active) { StopPan(); e.Handled = true; return; }
            if (!point.Properties.IsMiddleButtonPressed || point.Position.X < 0 || point.Position.Y < 0
                || point.Position.X > viewport.ActualWidth || point.Position.Y > viewport.ActualHeight) return;
            dataScroller ??= FindScroller(data);
            pan.Begin(point.Position.X, point.Position.Y);
            CapturePointer(e.Pointer); Focus(FocusState.Programmatic);
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeAll);
            panTimer.Start(); e.Handled = true;
        }), true);
        AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (!pan.Active) return;
            var point = e.GetCurrentPoint(viewport);
            if (!point.Properties.IsMiddleButtonPressed) { StopPan(); return; }
            pan.Move(point.Position.X, point.Position.Y);
            e.Handled = true;
        }), true);
        AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            if (!pan.Active || !pan.Held) return;
            if (e.GetCurrentPoint(viewport).Properties.IsMiddleButtonPressed) return;
            pan.Release(); StopPan();
            e.Handled = true;
        }), true);
        PointerCaptureLost += (_, _) => { if (pan.Held) StopPan(); };
        AddHandler(KeyDownEvent, new KeyEventHandler((_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && pan.Active) { StopPan(); e.Handled = true; }
        }), true);
        panTimer.Tick += (_, _) =>
        {
            var delta = pan.AutoStep;
            if (delta.X != 0) viewport.ChangeView(viewport.HorizontalOffset + delta.X, null, null, true);
            if (delta.Y != 0 && dataScroller is not null) dataScroller.ChangeView(null, dataScroller.VerticalOffset + delta.Y, null, true);
        };
    }
    private void StopPan()
    {
        pan.Stop(); panTimer.Stop(); ReleasePointerCaptures();
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }
    public async Task LoadAsync(LogPage? current)
    {
        active = true; page = current;
        if (current is null)
        {
            version++; loading?.Cancel(); data.ItemsSource = null; frozenHeader.Children.Clear(); scrollingHeader.Children.Clear();
            canvas.Width = 100;
            status.Text = "Parse a log to show messages in a table."; openButton.IsEnabled = false;
            return;
        }
        SyncColumns();
        await ReloadAsync();
    }

    private void SyncColumns()
    {
        var next = workspace.TableColumns;
        if (catalog.SequenceEqual(next)) return;
        var selected = choices.Where(c => c.Box.IsChecked == true).Select(c => c.Column.Key).ToHashSet(StringComparer.Ordinal);
        var first = catalog.Count == 0;
        catalog = next;
        if (catalog.Any(c => c.Key == "field:MTI") && !pinOverrides.Contains("field:MTI")) pinned.Add("field:MTI");
        var known = catalog.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var old in columnWidths.Keys.Where(k => !known.Contains(k)).ToArray()) columnWidths.Remove(old);
        updatingChoices = true;
        choices.Clear();
        var search = new TextBox { PlaceholderText = "Find a field or column…" };
        AutomationProperties.SetName(search, "Find table columns");
        var list = new StackPanel { Spacing = 2 };
        var defaultFields = catalog.Where(c => c.Key.StartsWith("field:", StringComparison.Ordinal)).Take(10).Select(c => c.Key).ToHashSet();
        foreach (var column in catalog)
        {
            var box = new CheckBox
            {
                Content = new TextBlock { Text = column.Header, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsChecked = first ? column.Key.StartsWith("meta:", StringComparison.Ordinal) || defaultFields.Contains(column.Key) : selected.Contains(column.Key)
            };
            var pin = new ToggleButton { Content = "Freeze", IsChecked = pinned.Contains(column.Key), IsEnabled = box.IsChecked == true, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(box, "Show " + column.Header + " column");
            AutomationProperties.SetName(pin, "Freeze " + column.Header + " column");
            ToolTipService.SetToolTip(box, column.Header);
            ToolTipService.SetToolTip(pin, "Keep this column visible while scrolling horizontally.");
            box.Click += (_, _) => { pin.IsEnabled = box.IsChecked == true; ColumnsChanged(); };
            pin.Click += (_, _) => SetPinned(column.Key, pin.IsChecked == true);
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(box); Grid.SetColumn(pin, 1); row.Children.Add(pin);
            list.Children.Add(row); choices.Add((column, box, pin, row));
        }
        search.TextChanged += (_, _) =>
        {
            foreach (var choice in choices) choice.Row.Visibility =
                choice.Column.Header.Contains(search.Text, StringComparison.OrdinalIgnoreCase) || choice.Column.Key.Contains(search.Text, StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
        };
        var all = new Button { Content = "Show all" }; var none = new Button { Content = "Clear" };
        void SetAll(bool value)
        {
            updatingChoices = true;
            foreach (var choice in choices) { choice.Box.IsChecked = value; choice.Pin.IsEnabled = value; }
            updatingChoices = false; ColumnsChanged();
        }
        all.Click += (_, _) => SetAll(true); none.Click += (_, _) => SetAll(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttons.Children.Add(all); buttons.Children.Add(none);
        var picker = new Grid { Width = 460, Height = 420, RowSpacing = 8 };
        picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        picker.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        picker.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        picker.Children.Add(search); Grid.SetRow(buttons, 1); picker.Children.Add(buttons);
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); picker.Children.Add(scroll);
        var presenter = new Style(typeof(FlyoutPresenter));
        presenter.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 560d));
        columnsButton.Flyout = new Flyout { Content = picker, FlyoutPresenterStyle = presenter };
        updatingChoices = false;
    }
    private void SetPinned(string key, bool freeze)
    {
        pinOverrides.Add(key);
        if (freeze) pinned.Add(key); else pinned.Remove(key);
        foreach (var choice in choices) choice.Pin.IsChecked = pinned.Contains(choice.Column.Key);
        ColumnsChanged();
    }
    private void ColumnsChanged()
    {
        if (updatingChoices) return;
        columnsButton.Content = $"Columns ({choices.Count(c => c.Box.IsChecked == true)})";
        columnDelay.Stop(); columnDelay.Start();
    }

    private async Task ReloadAsync()
    {
        if (!active || page is null) return;
        StopPan();
        var request = ++version;
        loading?.Cancel();
        using var cancellation = new CancellationTokenSource(); loading = cancellation;
        openButton.IsEnabled = false; data.IsHitTestVisible = false;
        var selected = choices.Where(c => c.Box.IsChecked == true).Select(c => c.Column)
            .OrderBy(c => pinned.Contains(c.Key) ? 0 : 1).ToArray();
        columnsButton.Content = $"Columns ({selected.Length})";
        status.Text = "Loading table values…";
        try
        {
            var current = page;
            var projected = await workspace.GetTablePageAsync(current.Rows, selected.Select(c => c.Key).ToArray(), cancellation.Token);
            if (request != version || !active) return;
            var fittedWidths = await MeasureColumnsAsync(projected, cancellation.Token);
            if (request != version || !active) return;
            visibleColumns = projected.Columns.Select(c =>
            {
                if (!columnWidths.TryGetValue(c.Key, out var column))
                {
                    column = new TableColumn(c, 80);
                    column.PropertyChanged += (_, _) => UpdateWidth();
                    columnWidths[c.Key] = column;
                }
                column.Fit(fittedWidths[c.Key]);
                return column;
            }).ToArray();
            frozenCount = visibleColumns.TakeWhile(c => pinned.Contains(c.Definition.Key)).Count();
            BuildHeaderAndTemplate();
            var summaries = current.Rows.ToDictionary(r => r.Id);
            var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
            (Brush Background, Brush Accent) RowColors(Windows.UI.Color accent)
            {
                var alpha = ActualTheme == ElementTheme.Light ? 20 : 35;
                var basis = ActualTheme == ElementTheme.Light ? 255 : 43;
                byte Blend(byte value) => (byte)((value * alpha + basis * (255 - alpha)) / 255);
                var background = Windows.UI.Color.FromArgb(255, Blend(accent.R), Blend(accent.G), Blend(accent.B));
                var system = new Windows.UI.ViewManagement.UISettings();
                return (new SolidColorBrush(highContrast ? system.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background) : background),
                    new SolidColorBrush(highContrast ? system.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground) : accent));
            }
            var requestColors = RowColors(Colors.DodgerBlue);
            var replyColors = RowColors(Colors.MediumPurple);
            var unknownColors = RowColors(Colors.Gray);
            var transparent = new SolidColorBrush(Colors.Transparent);
            var light = ActualTheme == ElementTheme.Light;
            var foreground = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
            var red = light ? Windows.UI.Color.FromArgb(255, 180, 35, 24) : Windows.UI.Color.FromArgb(255, 255, 119, 119);
            var yellow = light ? Windows.UI.Color.FromArgb(255, 128, 96, 0) : Windows.UI.Color.FromArgb(255, 255, 209, 102);
            var redBrush = new SolidColorBrush(highContrast ? foreground : red);
            var yellowBrush = new SolidColorBrush(highContrast ? foreground : yellow);
            Brush Fill(Windows.UI.Color c) => highContrast ? transparent : new SolidColorBrush(Windows.UI.Color.FromArgb(48, c.R, c.G, c.B));
            var redFill = Fill(red); var yellowFill = Fill(yellow);
            data.ItemsSource = selected.Length == 0 ? Array.Empty<TableRow>() : projected.Rows.Select(r =>
            {
                var summary = summaries[r.Id];
                var role = summary.Message?.Role;
                var colors = role == "request" ? requestColors : role is "reply" or "response" ? replyColors : unknownColors;
                TableCell Cell(TableColumn c)
                {
                    var key = c.Definition.Key;
                    var present = r.Values.TryGetValue(key, out var text);
                    var severity = r.Checks.GetValueOrDefault(key);
                    if (key == "meta:checks") severity = summary.Errors > 0 ? 2 : summary.Findings > 0 ? 1 : 0;
                    var marked = severity > 0 && (key.StartsWith("field:", StringComparison.Ordinal) || key == "meta:checks");
                    var description = !marked ? present ? "" : "Field not present in this message." : severity == 2 ? "Violation · open message details for field checks."
                        : "Warning / review · open message details for field checks.";
                    return new(text ?? "—", !marked ? transparent : severity == 2 ? redFill : severity == 1 ? yellowFill : transparent,
                        !marked ? transparent : severity == 2 ? redBrush : yellowBrush,
                        marked ? Visibility.Visible : Visibility.Collapsed, description);
                }
                return new TableRow(r.Id, visibleColumns.Select(Cell).ToArray(), visibleColumns, colors.Background, colors.Accent, layout);
            }).ToArray();
            if (data.Items.Count > 0) data.SelectedIndex = 0;
            status.Text = selected.Length == 0 ? "Choose at least one column to display." :
                $"{projected.Rows.Count:N0} messages on this page · right-click headers to freeze or fit · hold the middle button and point to scroll; release or Esc to stop."
                + " Field markers: red violation, yellow review."
                + (projected.ValuesShortened ? " Long values are shortened previews; open the message for full values." : "")
                + (workspace.TableColumnsLimited ? " Some field columns exceed table preview limits; open a message to inspect them." : "");
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex)
        {
            if (request == version) status.Text = "Unable to load table: " + ex.Message;
            ApplicationLogging.Logger.LogError(new EventId(202), ex, "Parser table failed");
        }
        finally
        {
            if (ReferenceEquals(loading, cancellation)) loading = null;
            if (request == version) { data.IsHitTestVisible = true; openButton.IsEnabled = data.SelectedItem is TableRow; }
        }
    }

    private async Task<Dictionary<string, double>> MeasureColumnsAsync(LogTablePage projected, CancellationToken token)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        var cache = new Dictionary<string, double>(StringComparer.Ordinal);
        var probe = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.NoWrap };
        var heading = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.NoWrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var available = new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity);
        var measured = 0;
        foreach (var column in projected.Columns)
        {
            token.ThrowIfCancellationRequested();
            heading.Text = column.Header + (pinned.Contains(column.Key) ? " 📌" : "");
            heading.Measure(available);
            var width = heading.DesiredSize.Width + 28;
            foreach (var row in projected.Rows)
            {
                if (width >= 1200) break;
                if (!row.Values.TryGetValue(column.Key, out var text)) continue;
                if (!cache.TryGetValue(text, out var textWidth))
                {
                    probe.Text = text; probe.Measure(available); textWidth = probe.DesiredSize.Width;
                    if (cache.Count < 8192) cache[text] = textWidth;
                    if (++measured % 64 == 0) { await Task.Yield(); token.ThrowIfCancellationRequested(); }
                }
                var padding = column.Key.StartsWith("field:", StringComparison.Ordinal) || column.Key == "meta:checks" ? 40 : 24;
                width = Math.Max(width, textWidth + padding);
            }
            result[column.Key] = Math.Clamp(Math.Ceiling(width), 80, 1200);
        }
        return result;
    }

    private void UpdateWidth()
    {
        canvas.Width = Math.Max(100, visibleColumns.Sum(c => c.Width) + 20);
        layout.FrozenWidth = visibleColumns.Take(frozenCount).Sum(c => c.Width);
        frozenHeaderPanel.Width = layout.FrozenWidth;
        frozenHeaderPanel.Visibility = layout.FrozenVisibility;
        scrollingHeader.Margin = layout.ScrollingMargin;
        layout.HorizontalOffset = viewport.HorizontalOffset;
        frozenHeaderTransform.X = layout.HorizontalOffset;
    }
    private void BuildHeaderAndTemplate()
    {
        frozenHeader.Children.Clear(); scrollingHeader.Children.Clear();
        var system = new Windows.UI.ViewManagement.UISettings();
        var headingColor = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast
            ? system.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)
            : ActualTheme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 242, 242, 242) : Windows.UI.Color.FromArgb(255, 48, 48, 48);
        frozenHeaderPanel.Background = new SolidColorBrush(headingColor);
        foreach (var (column, index) in visibleColumns.Select((c, i) => (c, i)))
        {
            var cell = new Grid { Background = new SolidColorBrush(headingColor) };
            cell.SetBinding(FrameworkElement.WidthProperty, new Binding { Source = column, Path = new PropertyPath(nameof(column.Width)) });
            var label = new TextBlock { Text = column.Definition.Header + (index < frozenCount ? " 📌" : ""), TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(8, 8, 12, 8) };
            cell.Children.Add(label);
            ToolTipService.SetToolTip(label, column.Definition.Header + (index < frozenCount ? " · frozen" : "") + ". Right-click to freeze/unfreeze or fit this column.");
            var menu = new MenuFlyout();
            var freeze = new MenuFlyoutItem { Text = index < frozenCount ? "Unfreeze column" : "Freeze column" };
            freeze.Click += (_, _) => SetPinned(column.Definition.Key, !pinned.Contains(column.Definition.Key));
            var fit = new MenuFlyoutItem { Text = "Fit to contents" };
            fit.Click += (_, _) => column.ResetWidth();
            menu.Items.Add(freeze); menu.Items.Add(fit); cell.ContextFlyout = menu;
            var thumb = new Thumb { Width = 8, IsTabStop = true };
            thumb.Template = (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'><Border Width='2' Margin='0,4' Background='{ThemeResource CardStrokeColorDefaultBrush}' /></Border></ControlTemplate>");
            AutomationProperties.SetName(thumb, "Resize " + column.Definition.Header + " column");
            ToolTipService.SetToolTip(thumb, "Drag to resize. Left / Right adjusts width; Home resets it.");
            thumb.DragDelta += (_, e) => column.Width += e.HorizontalChange;
            thumb.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Left) column.Width -= 16;
                else if (e.Key == Windows.System.VirtualKey.Right) column.Width += 16;
                else if (e.Key == Windows.System.VirtualKey.Home) column.ResetWidth();
                else return;
                e.Handled = true;
            };
            cell.Children.Add(new ColumnResizeHandle { Content = thumb, HorizontalAlignment = HorizontalAlignment.Right, Width = 8 });
            (index < frozenCount ? frozenHeader : scrollingHeader).Children.Add(cell);
        }
        var xaml = new StringBuilder("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Border Background='{Binding Background}' BorderBrush='{Binding Accent}' BorderThickness='4,0,0,0'><Grid><StackPanel Orientation='Horizontal' Margin='{Binding Layout.ScrollingMargin}'>");
        // Only numeric binding indexes are generated; profile text is never injected into XAML.
        void AddCell(int i) => xaml.Append($"<Border Width='{{Binding Columns[{i}].Width}}' Background='{{Binding Cells[{i}].Fill}}' BorderBrush='{{Binding Cells[{i}].CheckBrush}}' BorderThickness='0,0,1,2' Padding='8,6' ToolTipService.ToolTip='{{Binding Cells[{i}].StatusText}}'><Grid ColumnSpacing='6'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><TextBlock Text='●' FontSize='10' Margin='0,3,0,0' Foreground='{{Binding Cells[{i}].CheckBrush}}' Visibility='{{Binding Cells[{i}].MarkerVisibility}}' AutomationProperties.Name='{{Binding Cells[{i}].StatusText}}'/><TextBlock Grid.Column='1' Text='{{Binding Cells[{i}].Text}}' TextWrapping='Wrap' FontSize='14' VerticalAlignment='Top'/></Grid></Border>");
        for (int i = frozenCount; i < visibleColumns.Length; i++) AddCell(i);
        xaml.Append("</StackPanel><Grid Width='{Binding Layout.FrozenWidth}' Visibility='{Binding Layout.FrozenVisibility}' HorizontalAlignment='Left'><Grid.RenderTransform><TranslateTransform X='{Binding Layout.HorizontalOffset}'/></Grid.RenderTransform><Border Background='{Binding Background}' Margin='-4,0,0,0' BorderBrush='{Binding Accent}' BorderThickness='4,0,0,0'/><StackPanel Orientation='Horizontal'>");
        for (int i = 0; i < frozenCount; i++) AddCell(i);
        xaml.Append("</StackPanel></Grid></Grid></Border></DataTemplate>");
        data.ItemTemplate = (DataTemplate)XamlReader.Load(xaml.ToString());
        UpdateWidth();
    }
}
