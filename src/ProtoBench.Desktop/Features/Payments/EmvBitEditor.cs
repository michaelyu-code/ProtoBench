using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Text;
using ProtoBench.Desktop.Controls;
using ProtoBench.Utilities.Payments;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Payments;

public sealed class EmvBitEditor : StackPanel
{
    private readonly EmvWorkspace workspace;
    private readonly TextBlock feedback = Ui.Text("");
    private readonly AdaptiveColumns byteTabs = new() { MinimumColumnWidth = 76, MaximumColumns = int.MaxValue,
        ColumnSpacing = 6, RowSpacing = 6 };
    private readonly List<(EmvBit Bit, CheckBox Check)> checks = [];
    private readonly List<TextBlock> byteValues = [];
    private readonly List<(TextBlock Label, ToggleButton Button)> tabHeaders = [];
    private readonly Grid byteBody = new();
    private readonly List<FrameworkElement> byteContents = [];
    private int selectedByte;
    private string displayedProfile = "";
    private bool refreshing;

    public EmvBitEditor(EmvWorkspace workspace)
    {
        this.workspace = workspace;
        Spacing = 8;
        ToolTipService.SetToolTip(this, "Checked = 1 · Unchecked = 0 · Bits run from b8 (80) to b1 (01).");
        var actions = Ui.Row(Ui.Button("Start with zeroes", workspace.ResetBits), Ui.Button("Copy hex value", () =>
        {
            if (workspace.Bits.Count == 0) return;
            try
            {
                var data = new DataPackage();
                data.SetText(CurrentHex());
                Clipboard.SetContent(data);
                feedback.Text = "Hex value copied";
            }
            catch { feedback.Text = "Clipboard unavailable. Copy the hex from the input field."; }
        }));
        Children.Add(actions);
        Children.Add(feedback);
        var frame = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    BorderBrush="{ThemeResource ControlStrongStrokeColorDefaultBrush}"
                    Background="{ThemeResource LayerFillColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="8" Padding="8" />
            """);
        frame.Child = ToolLayout.Stack(byteTabs, byteBody);
        Children.Add(frame);
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(workspace.Bits)) Refresh();
        };
        Refresh();
    }

    private string CurrentHex() => string.Concat(workspace.Bits.GroupBy(b => b.Byte)
        .Select(g => g.Where(b => b.IsSet).Sum(b => 1 << (b.Bit - 1)).ToString("X2")));

    private void ArrangeBytes()
    {
        var index = selectedByte;
        if (index < 0 || index >= byteContents.Count) return;
        // Editors stay in one visual parent across resizing and byte selection.
        for (var i = 0; i < byteContents.Count; i++) byteContents[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSelectedTab()
    {
        for (var i = 0; i < tabHeaders.Count; i++)
        {
            var selected = selectedByte == i;
            tabHeaders[i].Button.IsChecked = selected;
            tabHeaders[i].Label.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    private ToggleButton CreateTabHeader(string text, int index)
    {
        var label = Ui.Text(text);
        label.TextAlignment = TextAlignment.Center;
        var header = new ToggleButton { Content = label, MinWidth = 0, Padding = new Thickness(4, 8, 4, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        header.Click += (_, _) => { selectedByte = index; UpdateSelectedTab(); ArrangeBytes(); };
        // Keep keyboard navigation within the byte selector, including wrapped rows.
        header.KeyDown += (_, e) =>
        {
            var next = e.Key switch
            {
                Windows.System.VirtualKey.Left => Math.Max(0, selectedByte - 1),
                Windows.System.VirtualKey.Right => Math.Min(tabHeaders.Count - 1, selectedByte + 1),
                Windows.System.VirtualKey.Home => 0,
                Windows.System.VirtualKey.End => tabHeaders.Count - 1,
                _ => -1
            };
            if (next < 0) return;
            selectedByte = next; UpdateSelectedTab(); ArrangeBytes();
            tabHeaders[next].Button.Focus(FocusState.Keyboard);
            e.Handled = true;
        };
        tabHeaders.Add((label, header));
        return header;
    }

    private void Refresh()
    {
        refreshing = true;
        try
        {
            Visibility = workspace.IsBitMode ? Visibility.Visible : Visibility.Collapsed;
            if (!workspace.IsBitMode) { displayedProfile = ""; return; }
            var definition = workspace.SelectedDefinition!;
            var byteCount = workspace.Bits.Count > 0 ? workspace.Bits.Count / 8 : Math.Max(definition.Bytes.Length, definition.MinimumLength);
            var signature = $"{definition.Tag}/{definition.ProfileId}/{byteCount}";
            if (displayedProfile != signature)
            {
                byteBody.Children.Clear();
                selectedByte = 0;
                byteContents.Clear();
                byteTabs.Children.Clear();
                checks.Clear();
                byteValues.Clear();
                tabHeaders.Clear();
                displayedProfile = signature;
                for (var i = 0; i < byteCount; i++)
                {
                    var heading = Ui.Text("", 16);
                    byteValues.Add(heading);
                    var byteRows = new StackPanel { Spacing = 2, Margin = new Thickness(8) };
                    byteRows.Children.Add(heading);
                    var bitRows = new AdaptiveColumns { MinimumColumnWidth = 220, RowSpacing = 2 };
                    for (var j = 0; j < 8; j++)
                    {
                        var meaning = workspace.Bits.Count > 0 ? workspace.Bits[i * 8 + j].Meaning : i < definition.Bytes.Length ? definition.Bytes[i][j] : "Raw coded data";
                        var bit = new EmvBit(i + 1, 8 - j, false, meaning);
                        var check = new CheckBox { Content = Ui.Text($"b{bit.Bit} · {bit.Meaning}"), HorizontalAlignment = HorizontalAlignment.Stretch };
                        void Changed(object sender, RoutedEventArgs args)
                        {
                            if (!refreshing) workspace.SetBit(bit.Byte, bit.Bit, check.IsChecked == true);
                        }
                        check.Checked += Changed;
                        check.Unchecked += Changed;
                        checks.Add((bit, check));
                        bitRows.Children.Add(check);
                    }
                    byteRows.Children.Add(bitRows);
                    byteContents.Add(byteRows);
                    byteBody.Children.Add(byteRows);
                    byteTabs.Children.Add(CreateTabHeader($"Byte {i + 1}", i));
                }
                byteTabs.RefreshLayout();
                UpdateSelectedTab();
                ArrangeBytes();
            }
            var valid = workspace.Bits.Count == checks.Count;
            feedback.Text = "";
            for (var i = 0; i < checks.Count; i++)
            {
                checks[i].Check.IsEnabled = valid;
                checks[i].Check.IsChecked = valid && workspace.Bits[i].IsSet;
                if (valid && checks[i].Check.Content is TextBlock label) label.Text = $"b{workspace.Bits[i].Bit} · {workspace.Bits[i].Meaning}";
            }
            for (var i = 0; i < byteValues.Count; i++)
            {
                var byteBits = valid ? workspace.Bits.Skip(i * 8).Take(8).ToArray() : [];
                var number = byteBits.Where(b => b.IsSet).Sum(b => 1 << (b.Bit - 1));
                byteValues[i].Text = valid
                    ? $"Byte {i + 1} · Hex {number:X2} · Binary {Convert.ToString(number, 2).PadLeft(8, '0')}"
                    : $"Byte {i + 1}";
                tabHeaders[i].Label.Text = valid ? $"Byte {i + 1} · {number:X2}" : $"Byte {i + 1}";
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                    tabHeaders[i].Button, tabHeaders[i].Label.Text);
            }
        }
        finally { refreshing = false; }
    }
}
