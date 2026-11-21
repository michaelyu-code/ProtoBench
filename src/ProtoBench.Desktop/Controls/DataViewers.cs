using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;
namespace ProtoBench.Desktop.Controls;

public sealed class RawTextViewer : TextBox
{
    public bool CompactViewport { get; set; } = true;
    private XamlRoot? observedRoot;
    public RawTextViewer()
    {
        IsReadOnly = true;
        AcceptsReturn = true;
        TextWrapping = TextWrapping.NoWrap;
        FontFamily = new FontFamily("Cascadia Mono");
        MinHeight = 140;
        MaxHeight = 280;
        Loaded += (_, _) =>
        {
            if (observedRoot is not null) observedRoot.Changed -= RootChanged;
            observedRoot = XamlRoot;
            if (observedRoot is not null) observedRoot.Changed += RootChanged;
            UpdateViewport();
        };
        Unloaded += (_, _) => { if (observedRoot is not null) observedRoot.Changed -= RootChanged; observedRoot = null; };
        RegisterPropertyChangedCallback(TextProperty, (_, _) => UpdateViewport());
        SizeChanged += (_, _) => UpdateViewport();
    }
    private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateViewport();
    private void UpdateViewport()
    {
        if (!CompactViewport) return;
        // Empty outputs should not reserve a large blank editor. Filled results
        // retain a bounded local viewport, even on short laptop windows.
        var height = XamlRoot?.Size.Height ?? 900;
        var minimum = string.IsNullOrEmpty(Text) ? 40 : height < 700 ? 64 : 80;
        if (MinHeight != minimum) MinHeight = minimum;
        var maximum = Math.Max(minimum, Math.Min(480, height * .45));
        if (MaxHeight != maximum) MaxHeight = maximum;
    }
}
public sealed class HexViewer : TextBox
{
    public HexViewer()
    {
        IsReadOnly = true;
        AcceptsReturn = true;
        FontFamily = new FontFamily("Cascadia Mono");
        MinHeight = 100;
        MaxHeight = 240;
    }
    public void Show(byte[] bytes)
    {
        Text = string.Join(Environment.NewLine, bytes.Chunk(16).Take(256).Select((line, i) => $"{i * 16:X8}  {Convert.ToHexString(line.AsSpan()).Chunk(2).Select(c => new string(c)).Aggregate("", (a, b) => a + b + " ")}"));
        if (bytes.Length > 4096)
            Text += "\nPreview limited to 4096 bytes.";
    }
}
public static class ParsedFieldTree
{
    public static void Show(TreeView tree, ParsedRecord? record)
    {
        // RootNodes templates receive the TreeViewNode, not its Content.
        // Explicitly present Content so WinUI does not display the node's type name.
        tree.ItemTemplate ??= (DataTemplate)Microsoft.UI.Xaml.Application.Current.Resources["ParsedFieldNodeTemplate"];
        tree.RootNodes.Clear();
        if (record is null)
            return;
        TreeViewNode Node(ParsedField field)
        {
            var text = new TextBlock { Text = $"{field.Name}: {field.Value}", Tag = field, TextWrapping = TextWrapping.Wrap };
            ToolTipService.SetToolTip(text, string.Join("\n", new[] { field.Description ?? "", $"Source byte {field.Source.ByteOffset} · {field.Source.ByteLength} bytes" }.Where(s => s.Length > 0)));
            var node = new TreeViewNode { Content = text };
            foreach (var child in field.Children ?? [])
                node.Children.Add(Node(child));
            return node;
        }
        foreach (var field in record.Fields)
            tree.RootNodes.Add(Node(field));
    }
}
