using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Payments;

public sealed class Iso8583BuilderViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    public string ToolId => "iso8583-builder";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (Iso8583BuilderWorkspace)workspace;
        var profiles = new ComboBox { Header = "jPOS XML specification", ItemsSource = vm.Profiles, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        profiles.SetBinding(ComboBox.SelectedIndexProperty, Bind(vm, nameof(vm.ProfileIndex)));
        var encoding = new ComboBox { Header = "EBCDIC code page (XML EBCDIC fields)", ItemsSource = new[] { "IBM1047 (jPOS default)", "CP500 (EBCDIC International)" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        encoding.SetBinding(ComboBox.SelectedIndexProperty, Bind(vm, nameof(vm.EbcdicCodePage)));
        var header = Ui.Input(vm, nameof(vm.HeaderHex), "Message header (hex)");
        header.MaxLength = 196608;
        void UpdateHeader()
        {
            header.Visibility = vm.HeaderLength > 0 ? Visibility.Visible : Visibility.Collapsed;
            header.Header = $"Message header (exactly {vm.HeaderLength} bytes, hex)";
        }
        UpdateHeader();
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.HeaderLength)) UpdateHeader(); };
        var chooseField = new ComboBox { Header = "Add a field from the specification", DisplayMemberPath = "Label", SelectedValuePath = "Id", HorizontalAlignment = HorizontalAlignment.Stretch };
        chooseField.SetBinding(ComboBox.ItemsSourceProperty, Bind(vm, nameof(vm.FieldChoices), BindingMode.OneWay));
        chooseField.SetBinding(ComboBox.SelectedValueProperty, Bind(vm, nameof(vm.SelectedFieldId)));
        var add = Ui.Button("Add field", vm.AddField);
        var rows = ToolLayout.Stack();
        var fieldEditors = new Dictionary<Iso8583BuilderField, Expander>();
        void RefreshFields()
        {
            rows.Children.Clear();
            foreach (var removed in fieldEditors.Keys.Where(row => !vm.Fields.Contains(row)).ToArray()) fieldEditors.Remove(removed);
            foreach (var row in vm.Fields)
            {
                if (fieldEditors.TryGetValue(row, out var existing))
                {
                    rows.Children.Add(existing);
                    continue;
                }
                var mode = new ComboBox { Header = "Value format", ItemsSource = new[] { "ASCII text value", "Hex (encoded value bytes)" }, HorizontalAlignment = HorizontalAlignment.Stretch };
                mode.SetBinding(ComboBox.SelectedIndexProperty, Bind(row, nameof(row.Mode)));
                var value = Ui.Input(row, nameof(row.Value), "Value (without length prefix)", true);
                value.MaxLength = 262144; value.MinHeight = 60; value.MaxHeight = 150;
                var digitCount = Ui.Input(row, nameof(row.DigitCount), "BCD digits (blank = all nibbles)");
                digitCount.MaxLength = 6;
                void DigitsVisibility() => digitCount.Visibility = row.ShowDigitCount ? Visibility.Visible : Visibility.Collapsed;
                row.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(row.ShowDigitCount)) DigitsVisibility(); };
                DigitsVisibility();
                var controls = ToolLayout.Stack(Ui.Text(row.Hint, 12), mode, value, digitCount);
                if (row.Definition.Id != 0) controls.Children.Add(Ui.Button("Remove field", () => vm.RemoveField(row)));
                var caption = Ui.Text("");
                void UpdateCaption()
                {
                    var preview = row.Value.ReplaceLineEndings(" ");
                    caption.Text = preview.Length == 0 ? "Click to edit · no value"
                        : (preview.Length > 72 ? preview[..72] + "…" : preview) + (row.Mode == 1 ? " · hex" : " · text");
                }
                row.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(row.Value) or nameof(row.Mode)) UpdateCaption(); };
                UpdateCaption();
                var editor = new Expander
                {
                    Header = ToolLayout.Stack(Ui.Text(row.Label), caption), Content = controls, IsExpanded = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(editor, row.Label);
                editor.Expanding += (_, _) =>
                {
                    foreach (var other in fieldEditors.Values.Where(other => other != editor)) other.IsExpanded = false;
                };
                fieldEditors.Add(row, editor);
                rows.Children.Add(editor);
            }
            add.IsEnabled = vm.AvailableFields.Count > 0;
        }
        vm.Fields.CollectionChanged += (_, _) => RefreshFields();
        RefreshFields();
        var inputs = ToolLayout.Stack(profiles, Ui.AsyncButton("Load jPOS XML…", async () => { if (await dialogs.OpenAsync() is { } path) vm.LoadProfile(path); }),
            encoding, header, Ui.Row(Ui.Primary(new Button { Content = "Build preview", Command = vm.BuildCommand }), new Button { Content = "Clear fields", Command = vm.ClearCommand }, Ui.Progress(vm)),
            Ui.BoundText(vm, nameof(vm.Error)), chooseField, add, rows);

        var hex = new RawTextViewer { Header = "Complete message · hex bytes", TextWrapping = TextWrapping.Wrap,
            CompactViewport = false, MinHeight = 120, MaxHeight = double.PositiveInfinity, VerticalAlignment = VerticalAlignment.Stretch };
        hex.SetBinding(TextBox.TextProperty, Bind(vm, nameof(vm.Output), BindingMode.OneWay));
        var ascii = new RawTextViewer { Header = "ASCII preview · non-printable bytes shown as \\xNN", TextWrapping = TextWrapping.Wrap,
            CompactViewport = false, MinHeight = 120, MaxHeight = double.PositiveInfinity, VerticalAlignment = VerticalAlignment.Stretch };
        ascii.SetBinding(TextBox.TextProperty, Bind(vm, nameof(vm.AsciiPreview), BindingMode.OneWay));
        var feedback = Ui.Text("");
        void Copy(string value)
        {
            if (value.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(value); Clipboard.SetContent(data); feedback.Text = "Copied"; }
            catch { feedback.Text = "Clipboard unavailable. Select and copy the preview manually."; }
        }
        var copyHex = Ui.Button("Copy hex message", () => Copy(vm.Output));
        var copyAscii = Ui.Button("Copy ASCII message", () => { if (vm.CanCopyAscii) Copy(vm.AsciiPreview); });
        copyAscii.SetBinding(Button.IsEnabledProperty, Bind(vm, nameof(vm.CanCopyAscii), BindingMode.OneWay));
        ToolTipService.SetToolTip(copyAscii, "Available when every message byte is printable ASCII. Use Copy hex message for binary or EBCDIC.");
        var tree = new TreeView { SelectionMode = TreeViewSelectionMode.None, ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["Iso8583NodeTemplate"] };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tree, "Built ISO 8583 fields");
        void RefreshPreview()
        {
            feedback.Text = "";
            copyHex.IsEnabled = vm.Output.Length > 0;
            tree.RootNodes.Clear();
            foreach (var field in vm.PreviewFields)
            {
                var node = new TreeViewNode { Content = new Iso8583ViewFactory.NodeText($"{(field.Number < 0 ? "Header" : $"Field {field.Number}")} · {field.Description}   ", field.Value) };
                node.Children.Add(new TreeViewNode { Content = new Iso8583ViewFactory.NodeText("Encoded field (prefix + value): ", field.RawValue) });
                node.Children.Add(new TreeViewNode { Content = new Iso8583ViewFactory.NodeText("Byte offset: ", field.Offset.ToString()) });
                tree.RootNodes.Add(node);
            }
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Output)) RefreshPreview(); };
        RefreshPreview();
        var summary = Ui.BoundText(vm, nameof(vm.Summary));
        var actions = ToolLayout.Stack(Ui.Row(copyHex, copyAscii), feedback);
        var inspection = new Expander { Header = "Inspect packed fields",
            Content = new ScrollViewer { Content = tree, MaxHeight = 240, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch },
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        var preview = new Grid { RowSpacing = 8 };
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        preview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        preview.Children.Add(summary);
        Grid.SetRow(hex, 1); preview.Children.Add(hex);
        Grid.SetRow(actions, 2); preview.Children.Add(actions);
        Grid.SetRow(ascii, 3); preview.Children.Add(ascii);
        Grid.SetRow(inspection, 4); preview.Children.Add(inspection);
        void ResizePreview()
        {
            preview.MinHeight = (summary.Visibility == Visibility.Visible ? summary.ActualHeight : 0)
                + actions.ActualHeight + inspection.ActualHeight + hex.MinHeight + ascii.MinHeight + 32;
        }
        summary.SizeChanged += (_, _) => ResizePreview();
        actions.SizeChanged += (_, _) => ResizePreview();
        inspection.SizeChanged += (_, _) => ResizePreview();
        ResizePreview();
        var results = ToolLayout.FittedSection("Message preview", preview);
        return ToolLayout.Page(vm.Title, "Select a specification, add field values, then build and inspect the packed message.",
            ToolLayout.Workspace(vm, ToolLayout.Section("Specification and fields", inputs), results, () => vm.Output.Length > 0, resultColumnWeight: 1.3),
            ToolLayout.Help(Ui.Text("The same user and plugin jPOS XML profiles as the parser are available here. Changing specification or Clear fields resets to MTI 0200. ASCII text values are packed using the XML field encoding (including BCD or EBCDIC); fixed numeric fields are zero-padded and fixed character fields space-padded. Hex values are already-encoded value bytes, excluding length prefixes. For variable BCD hex, specify a digit count when the last nibble is padding; blank counts every nibble. Bitmap indicators and length prefixes are generated automatically, and fields are packed in numeric order. Binary fields require their exact fixed length. Use IBM1047 or CP500 for XML EBCDIC fields. Preview is generated only after validation and parsing the result back. Editing clears the old preview. Hex is the lossless output; ASCII preview escapes non-printable bytes. No transport framing or MAC calculation is added. Custom/composite packagers remain unsupported.")));
    }
    private static Binding Bind(object source, string property, BindingMode mode = BindingMode.TwoWay)
        => new() { Source = source, Path = new PropertyPath(property), Mode = mode, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged };
}
