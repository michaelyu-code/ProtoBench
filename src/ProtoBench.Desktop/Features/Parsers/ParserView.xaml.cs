using Microsoft.UI.Xaml.Controls;
using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;
using ProtoBench.Desktop.Services;
using ProtoBench.Desktop.Controls;
using ProtoBench.Parsing.Engines;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Infrastructure.Logs;
using ProtoBench.Application.Services;

namespace ProtoBench.Desktop.Features.Parsers;

public sealed partial class ParserView : UserControl
{
    private IReadOnlyList<LogRecordSummary> rows = [];
    private ParsedRecord? selectedRecord;
    private CancellationTokenSource? selectionCancellation;
    private double? preferredDetailWidth;
    private readonly ParserResultPresentation presentation;
    private bool autoFitEnabled;
    private bool inspectingMessage;
    private bool hasResults;
    private bool layoutQueued;
    // Mutating a shared palette updates existing cards without rebuilding the message page.
    private readonly SolidColorBrush violationBrush = new(), warningBrush = new(), inboundBrush = new(), outboundBrush = new(), unknownDirectionBrush = new();
    private int specificationColumns;
    private bool verboseChecks;
    private IReadOnlyList<FieldCheck> checkDetails = [];
    private ParserTableView? tableView;
    private ITextRange? highlightedSource;
    private ITextCharacterFormat? originalSourceFormat;
    private sealed class MessageRow(LogRecordSummary record)
    {
        public LogRecordSummary Record { get; } = record;
        public string Number => Record.Message?.RecordNumber is { Length: > 0 } number ? number : Record.LineNumber.ToString();
        public string RecordLabel => Record.Message?.RecordNumber is { Length: > 0 } ? $"Record {Number}" : $"Source line {Number}";
        public string Mti => Record.Message?.MessageType ?? "—";
        public string Role => Record.Message?.Role switch { "request" => "Request", "reply" => "Reply", _ => "—" };
        public string DateTime => Record.Message?.LoggedDateTime ?? "—";
        public string Direction => Record.Message?.Direction ?? "—";
        public string DirectionSymbol => Direction switch { "IN" => "↓", "OUT" => "↑", _ => "↔" };
        public string DirectionDescription => $"{(Direction == "IN" ? "Inbound" : Direction == "OUT" ? "Outbound" : "Direction unknown")} · {Role}";
        public Brush? DirectionBrush { get; init; }
        public string Stan => Record.Stan;
        public string Terminal => Record.Terminal;
        public string Acceptor => Record.Acceptor;
        public string Sequence => Record.Message?.Sequence ?? "—";
        public string Checks => Record.Errors > 0 ? $"{Record.Errors} violations · {Record.Findings - Record.Errors} warnings"
            : Record.Findings > 0 ? $"{Record.Findings} warnings" : "No findings";
        public Visibility StatusVisibility => Record.Findings > 0 ? Visibility.Visible : Visibility.Collapsed;
        public Brush? StatusBrush { get; init; }
        public string CardTitle => Record.Message?.RecordNumber is { Length: > 0 }
            ? $"#{Number} · {Direction}" : $"Line {Number} · {(Direction is "IN" or "OUT" ? Direction : "?")}";
        public string CardMessage => $"{Mti} {Role}";
        public string CardTrace => $"STAN {Stan} · Seq {Sequence}";
        public string CardTerminal => $"Terminal {Terminal}";
        public string CardAcceptor => $"Acceptor {Acceptor}";
        public string Description => $"{RecordLabel} · {Mti} {Role} · {DateTime} · {Direction} · STAN {Stan}";
        public override string ToString() => Description;
    }
    private sealed class FieldRow
    {
        public ParsedField? Field { get; init; }
        public string Number { get; init; } = "";
        public string Name { get; init; } = "";
        public string Preview { get; init; } = "";
        public string DecodedSummary => Field?.Children is { Count: > 0 } children ? $"{children.Count} decoded items" : "";
        public Visibility DecodedSummaryVisibility => DecodedSummary.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string Validation { get; init; } = "";
        public Visibility StatusVisibility => Validation == "Good · no findings" ? Visibility.Collapsed : Visibility.Visible;
        public Brush? StatusBrush { get; init; }
        public string Explanation { get; init; } = "";
        public string Checks { get; init; } = "";
        public override string ToString() => $"DE {Number} · {Name}: {Preview} · {Validation}";
    }
    private static bool IsError(FieldFinding f) => f.Status is "Missing" or "Invalid" or "Forbidden";
    private sealed class DecodedRow(ParsedField field, int depth, string path)
    {
        public ParsedField Field { get; } = field;
        public string Name => Field.Name;
        public string Value => Field.Value;
        public string Description => Field.Description ?? "";
        public Thickness Indent => new(Math.Min(depth, 4) * 12, 0, 0, 0);
        public string SourceSummary => $"{path}\n{Description}\nSource byte {Field.Source.ByteOffset} · {Field.Source.ByteLength} bytes";
        public override string ToString() => $"{path}: {Value}";
    }

    private static IEnumerable<DecodedRow> DecodedRows(IReadOnlyList<ParsedField> fields, int depth = 0, string parent = "")
    {
        foreach (var field in fields)
        {
            var path = parent.Length == 0 ? field.Name : $"{parent} / {field.Name}";
            yield return new DecodedRow(field, depth, path);
            foreach (var child in DecodedRows(field.Children ?? [], depth + 1, path))
                yield return child;
        }
    }

    private static int Errors(ParsedRecord r) => r.Findings.Count(IsError);
    private Brush CheckBrush(bool violation, bool warning) => violation ? violationBrush : warning ? warningBrush : Foreground;
    private Brush DirectionBrush(string? direction) => direction switch
    {
        "IN" => inboundBrush, "OUT" => outboundBrush, _ => unknownDirectionBrush
    };

    private void UpdateBrushes()
    {
        var highContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var light = ActualTheme == ElementTheme.Light;
        var foreground = highContrast
            ? new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground)
            : default;
        violationBrush.Color = highContrast ? foreground : light ? Windows.UI.Color.FromArgb(255, 180, 35, 24) : Windows.UI.Color.FromArgb(255, 255, 119, 119);
        warningBrush.Color = highContrast ? foreground : light ? Windows.UI.Color.FromArgb(255, 128, 96, 0) : Windows.UI.Color.FromArgb(255, 255, 209, 102);
        inboundBrush.Color = highContrast ? foreground : light ? Windows.UI.Color.FromArgb(255, 0, 95, 184) : Windows.UI.Color.FromArgb(255, 102, 190, 255);
        outboundBrush.Color = highContrast ? foreground : light ? Windows.UI.Color.FromArgb(255, 112, 48, 160) : Windows.UI.Color.FromArgb(255, 208, 160, 255);
        unknownDirectionBrush.Color = highContrast ? foreground : light ? Windows.UI.Color.FromArgb(255, 96, 96, 96) : Windows.UI.Color.FromArgb(255, 180, 180, 180);
    }
    private MessageRow Card(LogRecordSummary record) => new(record)
    {
        StatusBrush = CheckBrush(record.Errors > 0, record.Findings > 0),
        DirectionBrush = DirectionBrush(record.Message?.Direction)
    };

    public ParserView(ParserWorkspace vm, FileDialogService dialogs)
    {
        InitializeComponent();
        UpdateBrushes();
        presentation = (ParserResultPresentation)Resources["ResultPresentation"];
        autoFitEnabled = vm.AutoFitResults;
        verboseChecks = vm.VerboseChecks;
        ResultsViewPicker.ItemsSource = new[] { "Message cards", "Table" };
        ResultsViewPicker.SelectedIndex = 0;
        BackToMessages.Click += (_, _) =>
        {
            inspectingMessage = false;
            ArrangePanes();
            Records.Focus(FocusState.Programmatic);
            MessagesPanel.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
        };
        Records.ItemClick += (_, _) =>
        {
            inspectingMessage = true;
            ArrangePanes();
            if (BackToMessages.Visibility == Visibility.Visible) BackToMessages.Focus(FocusState.Programmatic);
        };
        InputToggleButton.Click += (_, _) => InputPanel.IsExpanded = !InputPanel.IsExpanded;
        InputPanel.RegisterPropertyChangedCallback(Expander.IsExpandedProperty, (_, _) => ArrangePanes());
        Ui.Primary(ParseTextButton); Ui.Primary(ParsePathButton);
        ResultsViewPicker.SelectionChanged += (_, _) =>
        {
            var table = ResultsViewPicker.SelectedIndex == 1;
            BrowseLayout.Visibility = table ? Visibility.Collapsed : Visibility.Visible;
            TableHost.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
            if (table)
            {
                if (tableView is null)
                {
                    tableView = new ParserTableView(vm);
                    tableView.OpenMessage += id =>
                    {
                        var index = rows.ToList().FindIndex(r => r.Id == id);
                        if (index < 0) return;
                        ResultsViewPicker.SelectedIndex = 0;
                        Records.SelectedIndex = index;
                        inspectingMessage = true;
                        ArrangePanes();
                    };
                }
                // A collapsed populated table still incurs WinUI unload work when another
                // tool is selected. Keep its state, but attach its visual tree only in table mode.
                TableHost.Child = tableView;
                _ = tableView.LoadAsync(vm.Page);
            }
            else
            {
                tableView?.Cancel();
                TableHost.Child = null;
                if (rows.Count > 0 && Records.SelectedIndex < 0) Records.SelectedIndex = 0;
            }
            ArrangePanes();
        };
        FieldDetailTabs.SelectionChanged += (_, _) => ArrangePanes();
        Raw.Loaded += (_, _) => highlightedSource?.ScrollIntoView(PointOptions.Start);
        Raw.CopyingToClipboard += (_, args) =>
        {
            var selection = Raw.Document.Selection;
            CopyOriginalText(ParserOriginalText.SelectedText(selectedRecord?.RawText ?? "",
                selection.StartPosition, selection.EndPosition));
            args.Handled = true;
        };
        CopyOriginal.Click += (_, _) => CopyOriginalText(selectedRecord?.RawText ?? "");
        foreach (var text in new[] { FilePath, Detection, Diagnostics, Status, FieldExplanation }) ToolLayout.HideWhenEmpty(text);
        Progress.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        Progress.RegisterPropertyChangedCallback(ProgressRing.IsActiveProperty, (_, _) =>
            Progress.Visibility = Progress.IsActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed);
        FormatPicker.ItemsSource = new[] { "Automatic detection" }.Concat(vm.Parsers.Select(p => p.Name)).ToArray();
        FormatPicker.SelectedIndex = 0;
        FormatPicker.SelectionChanged += (_, _) => vm.SelectedFormatId = FormatPicker.SelectedIndex > 0 ? vm.Parsers[FormatPicker.SelectedIndex - 1].FormatId : null;
        DirectionPicker.ItemsSource = new[] { "All directions", "IN", "OUT" };
        RolePicker.ItemsSource = new[] { "All roles", "request", "reply" };
        SortPicker.ItemsSource = new[] { "Record", "Date/time (year unknown)", "STAN", "Card acceptor ID", "Terminal ID", "Sequence", "MTI" };
        DirectionPicker.SelectedIndex = RolePicker.SelectedIndex = SortPicker.SelectedIndex = 0;
        PageSizePicker.ItemsSource = new[] { 100, 250, 500, 1000, 2000 };
        PageSizePicker.SelectedItem = LogQuery.DefaultPageSize;
        bool updatingPage = false;
        bool refreshingRecords = false, suppressRecordScroll = false, scrollAfterParse = false;
        Records.BringIntoViewRequested += (_, args) => { if (suppressRecordScroll) args.Handled = true; };
        var searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        LogQuery ReadQuery(long pageIndex = 0) => new(SearchInput.Text, SearchField.Text.Trim(),
            DirectionPicker.SelectedIndex > 0 ? (string)DirectionPicker.SelectedItem : "",
            RolePicker.SelectedIndex > 0 ? (string)RolePicker.SelectedItem : "",
            SortPicker.SelectedIndex switch { 1 => "datetime", 2 => "11", 3 => "42", 4 => "41", 5 => "sequence", 6 => "mti", _ => "record" },
            Descending.IsChecked == true, pageIndex, PageSizePicker.SelectedItem is int size ? size : LogQuery.DefaultPageSize);
        void Refresh()
        {
            hasResults = vm.Page is not null;
            SetupIntroduction.Visibility = hasResults ? Visibility.Collapsed : Visibility.Visible;
            ResultsSection.Visibility = SearchToolbar.Visibility = hasResults ? Visibility.Visible : Visibility.Collapsed;
            PageContent.RowDefinitions[0].Height = hasResults ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
            PageContent.RowDefinitions[2].Height = hasResults ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            inspectingMessage = false;
            ArrangePanes();
            refreshingRecords = suppressRecordScroll = true;
            selectionCancellation?.Cancel();
            rows = vm.Page?.Rows ?? [];
            Records.ItemsSource = rows.Select(Card).ToArray();
            if (vm.Page is { } page)
            {
                var first = page.Matches == 0 ? 0 : page.PageIndex * page.PageSize + 1;
                RecordCount.Text = $"{first:N0}–{first + rows.Count - (rows.Count > 0 ? 1 : 0):N0} of {page.Matches:N0} matches · {page.Total:N0} indexed messages"
                    + (vm.IsTruncated ? " · partial plugin preview" : " · search covers the full indexed log");
                updatingPage = true;
                PageNumber.Maximum = page.PageCount;
                PageNumber.Value = page.PageIndex + 1;
                updatingPage = false;
                PageCaptionBottom.Text = $"Page {page.PageIndex + 1:N0} of {page.PageCount:N0}";
            }
            else
            {
                RecordCount.Text = "No input parsed"; PageCaptionBottom.Text = "";
                updatingPage = true;
                SearchInput.Text = SearchField.Text = "";
                DirectionPicker.SelectedIndex = RolePicker.SelectedIndex = SortPicker.SelectedIndex = 0;
                Descending.IsChecked = false;
                PageSizePicker.SelectedItem = LogQuery.DefaultPageSize;
                PageNumber.Maximum = PageNumber.Value = 1;
                updatingPage = false;
            }
            PreviousPage.IsEnabled = !vm.IsQuerying && vm.Page is { PageIndex: > 0 };
            NextPage.IsEnabled = !vm.IsQuerying && vm.Page is { } p && p.PageIndex + 1 < p.PageCount;
            NoMatches.Visibility = rows.Count == 0 && vm.Page is not null ? Visibility.Visible : Visibility.Collapsed;
            if (rows.Count > 0 && ResultsViewPicker.SelectedIndex != 1) Records.SelectedIndex = 0;
            else if (rows.Count == 0) ShowRecord(null, vm);
            if (ResultsViewPicker.SelectedIndex == 1 && tableView is not null) _ = tableView.LoadAsync(vm.Page);
            refreshingRecords = false;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => suppressRecordScroll = false);
        }
        void ScheduleSearch()
        {
            if (updatingPage) return;
            var active = (!string.IsNullOrWhiteSpace(SearchField.Text) ? 1 : 0) + (DirectionPicker.SelectedIndex > 0 ? 1 : 0) + (RolePicker.SelectedIndex > 0 ? 1 : 0)
                + (SortPicker.SelectedIndex > 0 || Descending.IsChecked == true ? 1 : 0);
            FilterButton.Content = active > 0 ? $"Filters and sort · {active} active" : "Filters and sort";
            searchDelay.Stop();
            vm.CancelSearch();
            selectionCancellation?.Cancel();
            if (!vm.IsBusy) searchDelay.Start();
        }
        searchDelay.Tick += async (_, _) => { searchDelay.Stop(); await vm.LoadPageAsync(ReadQuery()); };
        SearchInput.TextChanged += (_, _) => ScheduleSearch();
        SearchField.TextChanged += (_, _) => ScheduleSearch();
        DirectionPicker.SelectionChanged += (_, _) => ScheduleSearch();
        RolePicker.SelectionChanged += (_, _) => ScheduleSearch();
        SortPicker.SelectionChanged += (_, _) => ScheduleSearch();
        PageSizePicker.SelectionChanged += (_, _) => ScheduleSearch();
        Descending.Checked += (_, _) => ScheduleSearch();
        Descending.Unchecked += (_, _) => ScheduleSearch();
        PageNumber.ValueChanged += async (_, _) =>
        {
            if (!updatingPage && double.IsFinite(PageNumber.Value) && vm.Page is not null)
                await vm.LoadPageAsync(ReadQuery(Math.Max(0, (long)PageNumber.Value - 1)));
        };
        async Task TurnPage(int change)
        {
            searchDelay.Stop();
            await vm.LoadPageAsync(ReadQuery(Math.Max(0, (vm.Page?.PageIndex ?? 0) + change)));
            RecordCount.StartBringIntoView();
        }
        PreviousPage.Click += async (_, _) => await TurnPage(-1);
        NextPage.Click += async (_, _) => await TurnPage(1);
        BrowseLayout.SizeChanged += (_, _) => ScheduleArrangePanes();
        FieldLayout.SizeChanged += (_, _) => ScheduleArrangePanes();
        SizeChanged += (_, _) => ScheduleArrangePanes();
        TableHost.SizeChanged += (_, _) => ScheduleArrangePanes();
        DetailResizeHandle.DragDelta += (_, args) => ResizeDetails(-args.HorizontalChange);
        DetailResizeHandle.KeyDown += (_, args) =>
        {
            switch (args.Key)
            {
                case Windows.System.VirtualKey.Left: ResizeDetails(16); break;
                case Windows.System.VirtualKey.Right: ResizeDetails(-16); break;
                case Windows.System.VirtualKey.Home: preferredDetailWidth = null; ArrangePanes(); break;
                default: return;
            }
            args.Handled = true;
        };
        CloseFieldDetail.Click += (_, _) => { FieldList.SelectedItem = null; FieldList.Focus(FocusState.Programmatic); };
        ActualThemeChanged += (_, _) => { if (IsLoaded) UpdateBrushes(); };
        PreviousRecord.Click += async (_, _) =>
        {
            if (Records.SelectedIndex > 0) Records.SelectedIndex--;
            else if (vm.Page is { PageIndex: > 0 }) { await TurnPage(-1); Records.SelectedIndex = rows.Count - 1; }
        };
        NextRecord.Click += async (_, _) =>
        {
            if (Records.SelectedIndex + 1 < rows.Count) Records.SelectedIndex++;
            else if (vm.Page is { } p && p.PageIndex + 1 < p.PageCount) await TurnPage(1);
        };
        OpenButton.Click += async (_, _) =>
        {
            try
            {
                var file = await dialogs.OpenAsync();
                if (file is not null)
                {
                    PathInput.Text = file;
                    await vm.OpenAsync(file);
                }
            }
            catch (Exception ex) { Status.Text = ex.Message; }
        };
        CancelButton.Click += (_, _) => vm.CancelWork();
        ParseTextButton.Click += async (_, _) => await vm.ParseTextAsync(PasteInput.Text);
        ReparseFileButton.Click += async (_, _) => await vm.ReparseFileAsync();
        PasteInput.Paste += (_, args) =>
        {
            args.Handled = true;
            if (vm.IsBusy) return;
            // Defer until the native paste command releases the clipboard. Small pastes insert editable
            // text; only large pastes stay on disk, ready for the same Parse text action.
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (vm.IsBusy) return;
                var text = await vm.PreparePasteAsync(ClipboardLogInput.OpenAsync);
                if (text is not null)
                {
                    var start = PasteInput.SelectionStart;
                    var retainedLength = PasteInput.Text.Length - PasteInput.SelectionLength;
                    PasteInput.SelectedText = text;
                    var insertedLength = PasteInput.Text.Length - retainedLength;
                    PasteInput.SelectionStart = Math.Clamp(start + insertedLength, 0, PasteInput.Text.Length);
                    PasteInput.SelectionLength = 0;
                    PasteInput.Focus(FocusState.Programmatic);
                }
                else if (vm.HasStagedPaste) PasteInput.Text = "";
                else PasteNotice.Text = vm.Status;
            });
        };
        ClearTextButton.Click += (_, _) => { vm.ClearStagedPaste(); PasteInput.Text = ""; };
        ParsePathButton.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(PathInput.Text))
            {
                Status.Text = "Enter a local file path.";
                return;
            }
            await vm.OpenAsync(PathInput.Text.Trim().Trim('"'));
        };
        async Task ShowSelectedAsync(LogRecordSummary? summary, bool bringIntoView = true)
        {
            selectionCancellation?.Cancel();
            if (summary is null) { ShowRecord(null, vm); return; }
            using var cancellation = new CancellationTokenSource();
            selectionCancellation = cancellation;
            var token = cancellation.Token;
            try
            {
                var record = await vm.GetRecordAsync(summary.Id, token);
                token.ThrowIfCancellationRequested();
                ShowRecord(record, vm);
                if (bringIntoView) { inspectingMessage = true; ArrangePanes(); }
                if (bringIntoView) InspectorPanel.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0 });
                var related = await vm.GetRelatedAsync(summary.Id, record, token);
                token.ThrowIfCancellationRequested();
                Related.ItemsSource = related.Take(250).Select(Card).ToArray();
                RelatedSummary.Text = related.Count == 0 ? vm.RelatedUnavailableReason(record) ?? "No related message found in the indexed log."
                    : related.Count > 250 ? "Showing the first 250 related messages from the full log. Narrow the search to inspect more."
                    : $"{related.Count} candidate(s) matched across the full log by MTI, terminal, connection, STAN and time. Select to open.";
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Status.Text = ex.Message; RelatedSummary.Text = "Unable to load related messages."; }
            finally { if (ReferenceEquals(selectionCancellation, cancellation)) selectionCancellation = null; }
        }
        Records.SelectionChanged += async (_, _) => await ShowSelectedAsync((Records.SelectedItem as MessageRow)?.Record, !refreshingRecords);
        FieldList.SelectionChanged += (_, _) =>
        {
            ShowField(FieldList.SelectedItem as FieldRow);
        };
        CopyValue.Click += (_, _) =>
        {
            if (FieldList.SelectedItem is not FieldRow { Field: { } field }) return;
            var data = new DataPackage(); data.SetText(field.Value); Clipboard.SetContent(data);
        };
        ViewSource.Click += (_, _) =>
        {
            if (Fields.SelectedItem is DecodedRow subfield) RevealSource(subfield.Field);
            else if (FieldList.SelectedItem is FieldRow { Field: { } field }) RevealSource(field);
        };
        Related.ItemClick += async (_, args) =>
        {
            var target = ((MessageRow)args.ClickedItem).Record;
            var index = rows.ToList().FindIndex(r => r.Id == target.Id);
            if (index >= 0) Records.SelectedIndex = index;
            else await ShowSelectedAsync(target);
            InspectorTabs.SelectedIndex = 0;
        };
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.VerboseChecks) && verboseChecks != vm.VerboseChecks)
            {
                verboseChecks = vm.VerboseChecks;
                UpdateChecks(vm);
            }
            if (args.PropertyName == nameof(vm.AutoFitResults) && autoFitEnabled != vm.AutoFitResults)
            {
                autoFitEnabled = vm.AutoFitResults;
                preferredDetailWidth = null;
                ArrangePanes();
            }
            if (args.PropertyName == nameof(vm.IsBusy) && vm.IsBusy)
            {
                DiagnosticsPanel.IsExpanded = false;
            }
            Status.Text = vm.Status;
            FilePath.Text = vm.Path;
            Detection.Text = vm.Candidates;
            Diagnostics.Text = vm.Diagnostics;
            DiagnosticsPanel.Header = vm.DiagnosticSummary;
            InputToggleButton.Content = string.IsNullOrWhiteSpace(vm.Diagnostics) ? "Log input" : "Input · diagnostics";
            ToolTipService.SetToolTip(InputToggleButton, $"{vm.Candidates}\n{vm.Status}\n{vm.DiagnosticSummary}");
            DiagnosticsPanel.Visibility = string.IsNullOrWhiteSpace(vm.Diagnostics) ? Visibility.Collapsed : Visibility.Visible;
            if (args.PropertyName == nameof(vm.Diagnostics)) ArrangePanes();
            Progress.IsActive = vm.IsBusy || vm.IsQuerying;
            OpenButton.IsEnabled = !vm.IsBusy;
            ParsePathButton.IsEnabled = !vm.IsBusy;
            ReparseFileButton.IsEnabled = vm.CanReparseFile;
            FormatPicker.IsEnabled = !vm.IsBusy;
            ParseTextButton.IsEnabled = !vm.IsBusy;
            ClearTextButton.IsEnabled = !vm.IsBusy;
            PasteInput.IsEnabled = !vm.IsBusy;
            PasteInput.IsReadOnly = vm.HasStagedPaste;
            PasteInput.PlaceholderText = vm.HasStagedPaste ? "Large paste ready. Choose Parse text to continue, or Clear text to replace it." : "Paste or type log text here…";
            PasteNotice.Text = vm.StagedPasteInfo;
            SearchInput.IsEnabled = SearchField.IsEnabled = DirectionPicker.IsEnabled = RolePicker.IsEnabled =
                SortPicker.IsEnabled = PageSizePicker.IsEnabled = PageNumber.IsEnabled = Descending.IsEnabled = !vm.IsBusy;
            if (args.PropertyName == nameof(vm.IsQuerying))
            {
                PreviousPage.IsEnabled = !vm.IsQuerying && vm.Page is { PageIndex: > 0 };
                NextPage.IsEnabled = !vm.IsQuerying && vm.Page is { } p && p.PageIndex + 1 < p.PageCount;
            }
            if (args.PropertyName == nameof(vm.IsBusy) && !vm.IsBusy && scrollAfterParse)
            {
                scrollAfterParse = false;
                if (vm.AutoScrollResults)
                {
                    ResultsViewPicker.Focus(FocusState.Programmatic);
                    InputPanel.IsExpanded = false;
                    PageScroll.ChangeView(null, 0, null, disableAnimation: true);
                }
            }
            if (args.PropertyName != nameof(vm.Page))
                return;
            if (vm.Page is null) scrollAfterParse = false;
            else if (vm.IsBusy && vm.Page.Total > 0) scrollAfterParse = true;
            Refresh();
        };
        Unloaded += (_, _) => { searchDelay.Stop(); selectionCancellation?.Cancel(); tableView?.Cancel(); };
        Loaded += (_, _) =>
        {
            ReparseFileButton.IsEnabled = vm.CanReparseFile;
            UpdateBrushes();
            autoFitEnabled = vm.AutoFitResults;
            ArrangePanes();
            if (ResultsViewPicker.SelectedIndex == 1 && tableView is not null) _ = tableView.LoadAsync(vm.Page);
            if (Records.SelectedItem is MessageRow row && selectedRecord is null) _ = ShowSelectedAsync(row.Record, false);
        };
    }

    private void ShowRecord(ParsedRecord? record, ParserWorkspace vm)
    {
        selectedRecord = record;
        UpdateOriginal(() =>
        {
            ClearSourceHighlight();
            Raw.Document.SetText(TextSetOptions.None, ParserOriginalText.DisplayText(record?.RawText ?? ""));
            var paragraphs = Raw.Document.GetRange(0, int.MaxValue).ParagraphFormat;
            paragraphs.SpaceBefore = 0;
            paragraphs.SpaceAfter = 0;
            paragraphs.SetLineSpacing(LineSpacingRule.Single, 0);
        });
        CopyOriginal.IsEnabled = record is not null;
        SelectedTitle.Text = record is null ? "Select a message" : $"{(record.Message?.RecordNumber is { Length: > 0 } number ? $"Record {number}" : $"Source line {record.LineNumber}")} · {record.Message?.MessageType ?? "—"} {record.Message?.Role ?? ""}";
        SelectedSummary.Text = record is null ? "Its fields, checks and original text will appear here."
            : record.Message is { } m ? $"{m.LoggedDateTime} · {m.Direction} · STAN {MessageAnalysis.Value(record, "11")} · Sequence {m.Sequence}\nTerminal {MessageAnalysis.Value(record, "41")} · Card acceptor {MessageAnalysis.Value(record, "42")} · {m.SpecVersion}"
            : $"Source line {record.LineNumber} · {record.Fields.Count} fields";
        var errors = record is null ? 0 : Errors(record);
        var reviews = (record?.Findings.Count ?? 0) - errors;
        CheckSummary.Text = record is null ? "" : $"{record.Fields.Count} fields · {errors} issues · {reviews} checks to review";
        CheckSummary.Foreground = record is null ? Foreground : CheckBrush(errors > 0, reviews > 0);
        ChecksTab.Header = $"Checks ({record?.Findings.Count ?? 0})";
        PreviousRecord.IsEnabled = Records.SelectedIndex > 0 || vm.Page is { PageIndex: > 0 };
        NextRecord.IsEnabled = Records.SelectedIndex >= 0 && (Records.SelectedIndex + 1 < rows.Count || vm.Page is { } page && page.PageIndex + 1 < page.PageCount);
        var fields = new List<FieldRow>();
        foreach (var field in record?.Fields ?? [])
        {
            var findings = record!.Findings.Where(f => f.Field == field.DataElement || f.Field == field.Name).ToArray();
            fields.Add(new FieldRow
            {
                Field = field, Number = field.DataElement is "mti" or "bitmap" or null ? "—" : field.DataElement, Name = field.Name,
                Preview = field.Value,
                Validation = findings.FirstOrDefault(IsError)?.Status ?? (findings.Length > 0 ? "Warning" : "Good · no findings"),
                StatusBrush = CheckBrush(findings.Any(IsError), findings.Length > 0),
                Explanation = field.Description ?? "",
                Checks = findings.Length == 0 ? "Good · no findings" : string.Join("\n\n", findings.Select(f => $"{f.Status}: {f.Explanation}"))
            });
        }
        foreach (var missing in record?.Findings.Where(f => f.Status == "Missing") ?? [])
            fields.Add(new FieldRow { Number = missing.Field, Name = "Required field", Preview = "Absent from this log", Validation = "Missing",
                StatusBrush = CheckBrush(true, false), Checks = $"Missing: {missing.Explanation}" });
        FieldList.ItemsSource = fields;
        ShowField(null);
        UpdateChecks(vm);
        Related.ItemsSource = null;
        RelatedSummary.Text = record is null ? "Select a message to find related messages." : "Finding related messages across the indexed log…";
    }

    private void UpdateChecks(ParserWorkspace vm)
    {
        var record = selectedRecord;
        checkDetails = verboseChecks && record is not null ? vm.GetCheckDetails(record) : [];
        Findings.ItemsSource = verboseChecks && record is not null
            ? new[] { new TextBlock { Text = FieldCheckReport.Format(checkDetails.Select(c => c with { Check = $"DE {c.Field} · {c.Check}" }),
                record.Findings), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(0, 8, 0, 8) } }
            : record?.Findings.OrderByDescending(IsError).Select(f => new TextBlock
        {
            Text = $"{f.Status} · DE {f.Field}\n{f.Explanation}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8),
            Foreground = CheckBrush(IsError(f), !IsError(f))
        }).ToArray();
        foreach (var text in Findings.ItemsSource as IEnumerable<TextBlock> ?? [])
            text.SetBinding(TextBlock.FontSizeProperty, new Binding { Source = presentation, Path = new PropertyPath(nameof(presentation.BodyFontSize)) });
        FieldChecks.Text = FormatFieldChecks(FieldList.SelectedItem as FieldRow);
    }

    private string FormatFieldChecks(FieldRow? row)
    {
        if (row is null) return "";
        if (!verboseChecks) return row.Checks;
        var id = row.Field?.DataElement ?? row.Number;
        return FieldCheckReport.Format(checkDetails.Where(c => c.Field == id || c.Field == row.Name),
            selectedRecord?.Findings.Where(f => f.Field == id || f.Field == row.Name) ?? []);
    }

    private void ShowField(FieldRow? row)
    {
        FieldDetailTitle.Text = row is null ? "Field details" : $"DE {row.Number} · {row.Name}";
        FieldDetailPanel.Visibility = row is null ? Visibility.Collapsed : Visibility.Visible;
        FieldValue.Text = row?.Field?.Value ?? "";
        FieldExplanation.Text = row?.Explanation ?? "";
        FieldChecks.Text = FormatFieldChecks(row);
        FieldSpecification.Text = row?.Field?.Specification ?? "";
        specificationColumns = 0;
        int column = 0;
        foreach (var ch in FieldSpecification.Text)
        {
            if (ch == '\n') column = 0;
            else if (ch != '\r') column = Math.Min(2000, column + (ch == '\t' ? 4 : 1));
            specificationColumns = Math.Max(specificationColumns, column);
        }
        var hasSpecification = !string.IsNullOrWhiteSpace(FieldSpecification.Text);
        FieldSpecification.Visibility = hasSpecification ? Visibility.Visible : Visibility.Collapsed;
        NoSpecification.Visibility = hasSpecification ? Visibility.Collapsed : Visibility.Visible;
        FieldDetailTabs.SelectedIndex = 0;
        CopyValue.IsEnabled = ViewSource.IsEnabled = row?.Field is not null;
        Fields.SelectedItem = null;
        var decoded = DecodedRows(row?.Field?.Children ?? []).ToArray();
        Fields.ItemsSource = decoded;
        DecodedPanel.Visibility = decoded.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        NoDecodedFields.Visibility = decoded.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ArrangePanes();
    }

    private void ScheduleArrangePanes()
    {
        if (!IsLoaded || layoutQueued) return;
        layoutQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            layoutQueued = false;
            ArrangePanes();
        });
    }

    private void ArrangePanes()
    {
        if (!IsLoaded) return;
        var showing = FieldDetailPanel.Visibility == Visibility.Visible;
        var compactPage = ActualWidth < 720 || ActualHeight < 640;
        PageContent.Margin = new Thickness(compactPage ? 8 : 12);
        PageScroll.Visibility = !hasResults || InputPanel.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        PageScroll.MaxHeight = hasResults ? Math.Max(56, ActualHeight * 0.30) : double.PositiveInfinity;
        PasteInput.Height = ActualHeight < 640 ? 110 : 180;
        var availableHeight = BrowseLayout.ActualHeight;
        var shortInspector = availableHeight < 330;
        SelectedSummary.Visibility = CheckSummary.Visibility = shortInspector ? Visibility.Collapsed : Visibility.Visible;
        ToolTipService.SetToolTip(SelectedTitle, $"{SelectedSummary.Text}\n{CheckSummary.Text}");
        ToolTipService.SetToolTip(FieldDetailTitle, FieldDetailTitle.Text);
        if (ResultsViewPicker.SelectedIndex == 1) return;
        var layout = presentation.Arrange(BrowseLayout.ActualWidth, FieldLayout.ActualWidth, autoFitEnabled,
            showing, preferredDetailWidth, specificationColumns, availableHeight, Fields.Items.Count);
        var beside = layout.MessagesBeside;
        MessagesPanel.Visibility = beside || !inspectingMessage ? Visibility.Visible : Visibility.Collapsed;
        InspectorPanel.Visibility = beside || inspectingMessage ? Visibility.Visible : Visibility.Collapsed;
        BackToMessages.Visibility = !beside && inspectingMessage ? Visibility.Visible : Visibility.Collapsed;
        var leftWidth = beside ? new GridLength(layout.MessageWidth) : new GridLength(1, GridUnitType.Star);
        if (BrowseLayout.ColumnDefinitions[0].Width != leftWidth) BrowseLayout.ColumnDefinitions[0].Width = leftWidth;
        var rightWidth = beside ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        if (BrowseLayout.ColumnDefinitions[1].Width != rightWidth) BrowseLayout.ColumnDefinitions[1].Width = rightWidth;
        Grid.SetColumnSpan(MessagesPanel, beside ? 1 : 2);
        Grid.SetColumn(InspectorPanel, beside ? 1 : 0);
        Grid.SetRow(InspectorPanel, 0);
        Grid.SetColumnSpan(InspectorPanel, beside ? 1 : 2);
        var fieldBeside = layout.DetailsBeside;
        var detailWidth = layout.DetailWidth;
        var handleWidth = fieldBeside ? 12 : 0;
        if (FieldLayout.ColumnDefinitions[1].Width.Value != handleWidth)
            FieldLayout.ColumnDefinitions[1].Width = new GridLength(handleWidth);
        if (Math.Abs(FieldLayout.ColumnDefinitions[2].Width.Value - detailWidth) > 0.5)
            FieldLayout.ColumnDefinitions[2].Width = new GridLength(detailWidth);
        DetailResizeHandle.Visibility = fieldBeside ? Visibility.Visible : Visibility.Collapsed;
        DetailResizeCursor.Visibility = DetailResizeHandle.Visibility;
        Grid.SetColumn(FieldDetailPanel, fieldBeside ? 2 : 0);
        Grid.SetRow(FieldDetailPanel, 0);
        Grid.SetColumnSpan(FieldDetailPanel, fieldBeside ? 1 : 3);
        // Each pane owns its scroll area. Neither the list nor the detail header can be
        // scrolled out of the tab viewport by scrolling the other pane.
        var stacked = showing && !fieldBeside;
        FieldList.Visibility = stacked ? Visibility.Collapsed : Visibility.Visible;
        CloseFieldDetail.Content = stacked ? "← Fields" : "×";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(CloseFieldDetail, stacked ? "Back to fields" : "Close field details");
        // The selected narrow detail replaces the list in the same grid row.
        var listHeight = new GridLength(1, GridUnitType.Star);
        var detailHeight = new GridLength(0);
        if (FieldLayout.RowDefinitions[0].Height != listHeight) FieldLayout.RowDefinitions[0].Height = listHeight;
        if (FieldLayout.RowDefinitions[1].Height != detailHeight) FieldLayout.RowDefinitions[1].Height = detailHeight;
        FieldLayout.RowSpacing = 0;
    }

    private void ResizeDetails(double change)
    {
        if (DetailResizeHandle.Visibility != Visibility.Visible) return;
        preferredDetailWidth = FieldLayout.ColumnDefinitions[2].ActualWidth + change;
        ArrangePanes();
        preferredDetailWidth = FieldLayout.ColumnDefinitions[2].Width.Value;
    }

    private void SubfieldSourceMenu_Opening(object sender, object e)
    {
        if (sender is not MenuFlyout menu) return;
        var field = ((menu.Target as FrameworkElement)?.DataContext as DecodedRow)?.Field;
        if (menu.Items[0] is MenuFlyoutItem item)
        {
            item.Tag = field;
            item.IsEnabled = field is not null;
        }
    }

    private void SubfieldViewSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: ParsedField field }) RevealSource(field);
    }

    private void RevealSource(ParsedField field)
    {
        var raw = selectedRecord?.RawText ?? "";
        var source = field.Source;
        if (source.CharacterOffset < 0 || source.CharacterLength < 0 ||
            source.CharacterOffset > raw.Length - source.CharacterLength) return;
        // RichEdit uses one carriage return per paragraph, regardless of the log's newlines.
        var start = ParserOriginalText.DisplayOffset(raw, source.CharacterOffset);
        var end = ParserOriginalText.DisplayOffset(raw, source.CharacterOffset + source.CharacterLength);
        var range = Raw.Document.GetRange(start, end);
        UpdateOriginal(() =>
        {
            ClearSourceHighlight();
            originalSourceFormat = range.CharacterFormat.GetClone();
            range.CharacterFormat.BackgroundColor = Colors.Yellow;
            range.CharacterFormat.ForegroundColor = Colors.Black;
            highlightedSource = range;
        });
        Raw.Document.Selection.SetRange(start, start);
        InspectorTabs.SelectedIndex = 1;
        // Wait for the Original tab to be laid out before scrolling to the highlighted value.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(highlightedSource, range) || InspectorTabs.SelectedIndex != 1) return;
            Raw.UpdateLayout();
            range.ScrollIntoView(PointOptions.Start);
        });
    }

    private void UpdateOriginal(Action update)
    {
        // RichEdit also blocks programmatic text/format changes while read-only.
        Raw.IsReadOnly = false;
        try { update(); }
        finally { Raw.IsReadOnly = true; }
    }

    private static void CopyOriginalText(string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
    }

    private void ClearSourceHighlight()
    {
        if (highlightedSource is not null && originalSourceFormat is not null)
            highlightedSource.CharacterFormat = originalSourceFormat;
        highlightedSource = null;
        originalSourceFormat = null;
    }

    private static string Summary(ParsedRecord r) => r.Message is { } m
        ? $"{m.RecordNumber} · {m.LoggedDateTime} · {m.Direction} · {m.Sequence} · {m.MessageType} · {m.Role} · {MessageAnalysis.Value(r, "11")} · {MessageAnalysis.Value(r, "41")} · {MessageAnalysis.Value(r, "42")} [{r.Findings.Count} findings]"
        : $"{r.LineNumber,6}   {string.Join(" | ", r.Fields.Select(f => f.Name + " = " + f.Value))}";

}

