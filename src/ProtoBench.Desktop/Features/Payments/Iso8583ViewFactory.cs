using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Controls;
using ProtoBench.Desktop.Services;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Payments;

public sealed class Iso8583ViewFactory(FileDialogService dialogs) : IWorkspaceViewFactory
{
    // Keep node content as data: recycled TreeView containers must not share live UI elements.
    public sealed record NodeText(string Label, string Value);
    public string ToolId => "iso8583-parser";
    public FrameworkElement Create(ToolWorkspace workspace)
    {
        var vm = (Iso8583Workspace)workspace;
        var profiles = new ComboBox { Header = "jPOS XML packager", ItemsSource = vm.Profiles, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
        profiles.SetBinding(ComboBox.SelectedIndexProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.ProfileIndex)), Mode = BindingMode.TwoWay });
        var load = Ui.Button("Load jPOS XML…", async () => { if (await dialogs.OpenAsync() is { } path) vm.LoadProfile(path); });
        var mode = new ComboBox { Header = "Input encoding", ItemsSource = new[] { "ASCII text (preserve spaces)", "Hex bytes (whitespace allowed)" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        mode.SelectionChanged += (_, _) => vm.InputMode = mode.SelectedIndex;
        var ebcdic = new ComboBox { Header = "EBCDIC code page (for XML EBCDIC fields)",
            ItemsSource = new[] { "IBM1047 (jPOS default)", "CP500 (EBCDIC International)" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        ebcdic.SetBinding(ComboBox.SelectedIndexProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.EbcdicCodePage)), Mode = BindingMode.TwoWay });
        var input = Ui.Input(vm, nameof(vm.Input), "Raw ISO 8583 message", true);
        input.MaxLength = 2097152; input.MinHeight = 160; input.MaxHeight = 360;
        var output = new RawTextViewer { Header = "Copyable text", MinHeight = 180, MaxHeight = 480, TextWrapping = TextWrapping.Wrap };
        output.SetBinding(TextBox.TextProperty, new Binding { Source = vm, Path = new PropertyPath(nameof(vm.Output)), Mode = BindingMode.OneWay });
        var copyStatus = Ui.Text("");
        var copy = Ui.Button("Copy result", () =>
        {
            if (vm.Output.Length == 0) return;
            try { var data = new DataPackage(); data.SetText(vm.Output); Clipboard.SetContent(data); copyStatus.Text = "Copied"; }
            catch { copyStatus.Text = "Clipboard unavailable. Select and copy the result manually."; }
        });
        var tree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.None,
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["Iso8583NodeTemplate"],
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tree, "ISO 8583 fields");
        TreeViewNode Property(string label, string value)
        {
            return new TreeViewNode { Content = new NodeText(label + ": ", value.Length == 0 ? "(empty)" : value) };
        }
        void Expand(int levels)
        {
            void Visit(TreeViewNode node, int depth)
            {
                node.IsExpanded = depth < levels && node.Children.Count > 0;
                foreach (var child in node.Children) Visit(child, depth + 1);
            }
            foreach (var node in tree.RootNodes) Visit(node, 0);
        }
        var expansion = Ui.Row(Ui.Button("Expand all", () => Expand(int.MaxValue)),
            Ui.Button("Collapse all", () => Expand(0)), Ui.Button("Expand level 1", () => Expand(1)));
        void RefreshResults()
        {
            copyStatus.Text = "";
            tree.RootNodes.Clear();
            foreach (var row in vm.DisplayFields)
            {
                var field = new TreeViewNode { Content = new NodeText(row.Heading + "   ", row.Field.Value.Length == 0 ? "(empty)" : row.Field.Value), IsExpanded = true };
                field.Children.Add(Property("Source value", row.SourceValue));
                var wire = new TreeViewNode { Content = new NodeText("Wire details", "") };
                if (row.Field.PrefixByteLength > 0)
                {
                    wire.Children.Add(Property("Length prefix", row.LengthPrefix));
                    wire.Children.Add(Property("Source field (prefix + value)", row.SourceField));
                }
                wire.Children.Add(Property("Raw hex (prefix + value)", row.Field.RawValue));
                wire.Children.Add(new TreeViewNode { Content = new NodeText(row.SourcePosition, "") });
                field.Children.Add(wire);
                tree.RootNodes.Add(field);
            }
            if (vm.RemainingHex.Length > 0) tree.RootNodes.Add(Property("Unparsed bytes (hex)", vm.RemainingHex));
            expansion.Visibility = tree.RootNodes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.Output)) RefreshResults(); };
        RefreshResults();
        var inputs = ToolLayout.Stack(profiles, load, mode, ebcdic, input,
            Ui.Row(Ui.Primary(new Button { Content = "Parse", Command = vm.ParseCommand }), new Button { Content = "Clear inputs", Command = vm.ClearCommand }),
            Ui.BoundText(vm, nameof(vm.Notice)), Ui.Progress(vm));
        var results = ToolLayout.Section("Results", Ui.BoundText(vm, nameof(vm.Notice)), Ui.Row(copy, copyStatus), expansion, tree,
            new Expander { Header = "Full result as text", Content = output, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        return ToolLayout.Page(vm.Title, "Inspect one raw message using a jPOS GenericPackager XML definition.",
            ToolLayout.Workspace(vm, ToolLayout.Section("Inputs", inputs), results, () => vm.Output.Length > 0, resultColumnWeight: 1.4),
            ToolLayout.Help(Ui.Text("Paste the message starting at its MTI, excluding transport framing unless headerLength is defined in the XML. Use Hex bytes for EBCDIC, binary or BCD messages. The XML class selects each field's representation; the EBCDIC code page changes only EBCDIC text, prefixes and encoded bitmaps. Choose CP500 for EBCDIC 500 hex, or IBM1047 for jPOS defaults. Binary fields are never translated. Text mode preserves spaces and line endings. Supports ASCII/EBCDIC text, signed amounts, packed BCD (including F padding), literal/encoded-hex binary, L/LL/LLL/LLLL character/binary prefixes, and primary/secondary/tertiary bitmaps. Unsupported custom/composite packagers stop with recovered fields retained. Partial fields retain source bytes and a warning. Load XML applies to this tab; plugin and user definitions/iso8583/*.xml load at startup. No Java code or external XML resources are loaded.")));
    }
}
