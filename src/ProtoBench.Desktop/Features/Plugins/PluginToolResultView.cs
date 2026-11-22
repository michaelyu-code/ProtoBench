using System.Globalization;
using System.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using ProtoBench.Core.Documents;
using ProtoBench.Desktop.Controls;
using ProtoBench.PluginSdk;
using Windows.ApplicationModel.DataTransfer;

namespace ProtoBench.Desktop.Features.Plugins;

/// <summary>Shared host presentation for portable plugins; no WinUI dependency is needed in the plugin.</summary>
public sealed class PluginToolResultView : ContentControl
{
    public PluginToolResultView() => HorizontalContentAlignment = HorizontalAlignment.Stretch;

    public void Show(PluginToolResult? result)
    {
        if (result is null) { Content = Ui.Text("Run the tool to see results."); return; }
        var panel = ToolLayout.Stack();
        if (result.Search is { } search) ShowSearch(panel, search);
        else if (result.Decoding is { } decoding) ShowDecoding(panel, decoding);
        else
        {
            panel.Children.Add(new RawTextViewer { Header = "Result", Text = result.Text, TextWrapping = TextWrapping.Wrap });
            if (result.Text.Length > 0) panel.Children.Add(CopyAction("Copy result", () => result.Text));
            if (result.Fields is { Count: > 0 })
            {
                var tree = new TreeView { MaxHeight = 360 };
                ParsedFieldTree.Show(tree, new ParsedRecord(0, "", result.Fields));
                panel.Children.Add(tree);
            }
        }
        if ((result.Search is not null || result.Decoding is not null) && result.Text.Length > 0)
            panel.Children.Add(Expand("Raw output", ToolLayout.Stack(
                new RawTextViewer { Text = result.Text, TextWrapping = TextWrapping.Wrap }, CopyAction("Copy raw output", () => result.Text))));
        Content = panel;
    }

    private static void ShowSearch(StackPanel panel, ToolSearchResult search)
    {
        var count = search.TotalMatches is { } total ? $"Showing {search.Hits.Count} of {total} results" : $"Showing {search.Hits.Count} results";
        panel.Children.Add(Ui.Text(search.Query.Length > 0 ? $"{count} for “{search.Query}”" : count));
        if (search.Hits.Count == 0) { panel.Children.Add(Selectable(search.EmptyMessage)); return; }
        panel.Children.Add(Ui.Text("Sorted by relevance", 12));
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single, MaxHeight = 440,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Ranked search results");
        foreach (var hit in search.Hits)
        {
            var title = Ui.Text(hit.Title); title.FontWeight = FontWeights.SemiBold;
            var item = ToolLayout.Stack(title);
            if (hit.Reference is { } reference) item.Children.Add(Ui.Text(ReferenceLabel(reference), 12));
            item.Children.Add(Highlighted(hit.Excerpt, search.HighlightTerms));
            list.Items.Add(new ListViewItem { Content = item, HorizontalContentAlignment = HorizontalAlignment.Stretch });
        }
        var preview = ToolLayout.Stack();
        void Select()
        {
            preview.Children.Clear();
            if (list.SelectedIndex < 0 || list.SelectedIndex >= search.Hits.Count) return;
            var hit = search.Hits[list.SelectedIndex];
            preview.Children.Add(Ui.Text(hit.Title, 18));
            if (hit.Reference is { } reference) preview.Children.Add(Ui.Text(ReferenceLabel(reference), 12));
            preview.Children.Add(Highlighted(hit.Passage, search.HighlightTerms));
            preview.Children.Add(CopyAction("Copy passage", () => string.Join(Environment.NewLine,
                new[] { hit.Title, hit.Reference is { } source ? ReferenceLabel(source) : "", hit.Passage }.Where(s => s.Length > 0))));
            if (hit.Score is { } score)
                preview.Children.Add(Expand("Ranking details", Ui.Text($"Relevance score: {score.ToString("G6", CultureInfo.InvariantCulture)}")));
        }
        list.SelectionChanged += (_, _) => Select();
        list.SelectedIndex = 0;
        panel.Children.Add(ToolLayout.Columns(280, list, preview));
    }

    private static void ShowDecoding(StackPanel panel, ToolDecodeResult decoding)
    {
        panel.Children.Add(Ui.Text(decoding.Field, 18));
        panel.Children.Add(Ui.Text(decoding.Status switch
        {
            ToolDecodeStatus.PartiallyDecoded => "Partially decoded",
            ToolDecodeStatus.InvalidInput => "Invalid input",
            ToolDecodeStatus.MissingContext => "Missing context",
            ToolDecodeStatus.Unmapped => "Unmapped value",
            _ => "Decoded"
        }));
        panel.Children.Add(Selectable(decoding.RawValue));
        panel.Children.Add(Selectable(decoding.Summary));
        foreach (var warning in decoding.Warnings) panel.Children.Add(Selectable($"Warning: {warning}"));
        if (decoding.Components.Count > 0)
        {
            // Reflow each component into labeled vertical values at narrow widths; never truncate meanings.
            foreach (var component in decoding.Components) panel.Children.Add(Component(component));
        }
        if (decoding.ContextUsed.Count > 0)
        {
            panel.Children.Add(Ui.Text("Context used", 16));
            foreach (var context in decoding.ContextUsed) panel.Children.Add(Selectable($"{context.Name}: {context.Value}"));
        }
        foreach (var reference in decoding.References) panel.Children.Add(Reference(reference));
        panel.Children.Add(CopyAction("Copy decoded result", () => DecodeText(decoding)));
    }

    private static FrameworkElement Component(ToolDecodeComponent component)
    {
        var name = ToolLayout.Stack(Ui.Text("Component", 12), Selectable(component.Name), Ui.Text(component.Position, 12));
        var columns = ToolLayout.Columns(150, name,
            ToolLayout.Stack(Ui.Text("Raw value", 12), Selectable(component.RawValue)),
            ToolLayout.Stack(Ui.Text("Meaning", 12), Selectable(component.Meaning)));
        columns.MaximumColumns = 3;
        columns.SizeChanged += (_, _) =>
        {
            // A component is either one labeled row or one labeled stack, never a misleading two-column wrap.
            columns.MaximumColumns = columns.ActualWidth < 474 ? 1 : 3;
            columns.RefreshLayout();
        };
        var panel = ToolLayout.Stack(columns);
        if (component.Reference is { } reference) panel.Children.Add(Reference(reference));
        if (component.Children.Count > 0)
        {
            var children = ToolLayout.Stack();
            foreach (var child in component.Children) children.Children.Add(Component(child));
            panel.Children.Add(Expand($"{component.Name} · subcomponents", children));
        }
        return panel;
    }

    private static FrameworkElement Reference(ToolSpecReference reference) => Expand("Specification reference",
        ToolLayout.Stack(Selectable(ReferenceLabel(reference)), Selectable(reference.Passage)));

    private static string ReferenceLabel(ToolSpecReference reference) =>
        string.Join(" · ", new[] { reference.Specification, reference.Location }.Where(s => s.Length > 0));

    private static Expander Expand(string title, UIElement content) => new()
    {
        Header = title, Content = content, IsExpanded = false,
        HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch
    };

    private static TextBlock Selectable(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    private static TextBlock Highlighted(string text, IReadOnlyList<string> terms)
    {
        var block = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        // Literal ordinal matches: spec/query content is never interpreted as markup or a regular expression.
        var ranges = new bool[text.Length];
        foreach (var term in terms.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            for (var start = 0; start <= text.Length - term.Length;)
            {
                var index = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                Array.Fill(ranges, true, index, term.Length);
                start = index + term.Length;
            }
        for (var start = 0; start < text.Length;)
        {
            var end = start + 1;
            while (end < text.Length && ranges[end] == ranges[start]) end++;
            block.Inlines.Add(new Run { Text = text[start..end], FontWeight = ranges[start] ? FontWeights.Bold : FontWeights.Normal });
            start = end;
        }
        return block;
    }

    private static FrameworkElement CopyAction(string label, Func<string> value)
    {
        var feedback = Ui.Text("", 12);
        return ToolLayout.Stack(Ui.Button(label, () =>
        {
            try { var data = new DataPackage(); data.SetText(value()); Clipboard.SetContent(data); feedback.Text = "Copied"; }
            catch { feedback.Text = "Clipboard unavailable. Select and copy the text manually."; }
        }), feedback);
    }

    private static string DecodeText(ToolDecodeResult result)
    {
        var text = new StringBuilder().AppendLine(result.Field).AppendLine(result.Status.ToString())
            .AppendLine(result.RawValue).AppendLine(result.Summary);
        void Add(ToolDecodeComponent component, string indent)
        {
            text.AppendLine($"{indent}{component.Name} [{component.Position}]: {component.RawValue} — {component.Meaning}");
            if (component.Reference is { } reference) text.AppendLine(ReferenceLabel(reference)).AppendLine(reference.Passage);
            foreach (var child in component.Children) Add(child, indent + "  ");
        }
        foreach (var component in result.Components) Add(component, "");
        foreach (var warning in result.Warnings) text.AppendLine($"Warning: {warning}");
        foreach (var context in result.ContextUsed) text.AppendLine($"{context.Name}: {context.Value}");
        foreach (var reference in result.References) text.AppendLine(ReferenceLabel(reference)).AppendLine(reference.Passage);
        return text.ToString().TrimEnd();
    }
}
