using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Text;
using ProtoBench.Desktop.Controls;
using ProtoBench.Utilities.Payments;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Payments;

public sealed class EmvBitEditor : StackPanel
{
    private readonly EmvWorkspace workspace;
    private readonly TextBlock value = Ui.Text("", 18);
    private readonly TextBlock feedback = Ui.Text("");
    private readonly TabView byteTabs = new()
    {
        IsAddTabButtonVisible = false,
        CanDragTabs = false,
        CanReorderTabs = false,
        TabWidthMode = TabViewWidthMode.SizeToContent
    };
    private readonly List<(EmvBit Bit, CheckBox Check)> checks = [];
    private readonly List<TextBlock> byteValues = [];
    private readonly List<(TextBlock Label, Border Indicator)> tabHeaders = [];
    private string displayedProfile = "";
    private bool refreshing;

    public EmvBitEditor(EmvWorkspace workspace)
    {
        this.workspace = workspace;
        Spacing = 8;
        Children.Add(Ui.Text("Interactive bits", 18));
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
        Children.Add(ToolLayout.Columns(260, value, actions));
        Children.Add(feedback);
        var frame = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    BorderBrush="{ThemeResource ControlStrongStrokeColorDefaultBrush}"
                    Background="{ThemeResource LayerFillColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="8" Padding="8" />
            """);
        frame.Child = byteTabs;
        Children.Add(frame);
        byteTabs.SelectionChanged += (_, _) => UpdateSelectedTab();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(workspace.Bits)) Refresh();
        };
        Refresh();
    }

    private string CurrentHex() => string.Concat(workspace.Bits.GroupBy(b => b.Byte)
        .Select(g => g.Where(b => b.IsSet).Sum(b => 1 << (b.Bit - 1)).ToString("X2")));

    private void UpdateSelectedTab()
    {
        for (var i = 0; i < tabHeaders.Count; i++)
        {
            var selected = byteTabs.SelectedIndex == i;
            tabHeaders[i].Indicator.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            tabHeaders[i].Label.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    private Border CreateTabHeader(string text)
    {
        var header = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    BorderBrush="{ThemeResource ControlStrongStrokeColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="4" MinWidth="104" Margin="2,2,2,4">
                <StackPanel>
                    <TextBlock FontSize="14" Margin="12,8" />
                    <Border Height="3" Background="{ThemeResource AccentFillColorDefaultBrush}" />
                </StackPanel>
            </Border>
            """);
        var content = (StackPanel)header.Child;
        var label = (TextBlock)content.Children[0];
        label.Text = text;
        tabHeaders.Add((label, (Border)content.Children[1]));
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
                byteTabs.TabItems.Clear();
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
                    byteTabs.TabItems.Add(new TabViewItem
                    {
                        Header = CreateTabHeader($"Byte {i + 1}"),
                        IsClosable = false,
                        Content = byteRows
                    });
                }
                byteTabs.SelectedIndex = 0;
                UpdateSelectedTab();
            }
            var valid = workspace.Bits.Count == checks.Count;
            value.Text = valid ? $"Hex value: {CurrentHex()}" : "Enter a complete hex value, or start with zeroes.";
            feedback.Text = valid ? "Checked = 1 · Unchecked = 0 · Bits run from b8 (80) to b1 (01)." : "";
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
                    (TabViewItem)byteTabs.TabItems[i], tabHeaders[i].Label.Text);
            }
        }
        finally { refreshing = false; }
    }
}
