using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using ProtoBench.Parsing;

namespace ProtoBench.Desktop.Features.Parsers;

public sealed class MessageCompareStatusVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is FieldComparisonStatus status && parameter?.ToString() switch
        {
            "Changed" => status == FieldComparisonStatus.Changed,
            "Missing" => status is FieldComparisonStatus.MissingFromMessage1 or FieldComparisonStatus.MissingFromMessage2,
            _ => status != FieldComparisonStatus.Match
        };
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Highlights the differing span without splitting emoji or combining characters.</summary>
public sealed class MessageCompareHighlight : DependencyObject
{
    public static readonly DependencyProperty FirstProperty = DependencyProperty.RegisterAttached("First", typeof(object), typeof(MessageCompareHighlight),
        new PropertyMetadata(null, (d, e) => Update(d, e.NewValue, true)));
    public static readonly DependencyProperty SecondProperty = DependencyProperty.RegisterAttached("Second", typeof(object), typeof(MessageCompareHighlight),
        new PropertyMetadata(null, (d, e) => Update(d, e.NewValue, false)));
    public static object GetFirst(DependencyObject target) => target.GetValue(FirstProperty);
    public static void SetFirst(DependencyObject target, object value) => target.SetValue(FirstProperty, value);
    public static object GetSecond(DependencyObject target) => target.GetValue(SecondProperty);
    public static void SetSecond(DependencyObject target, object value) => target.SetValue(SecondProperty, value);

    private static void Update(DependencyObject target, object value, bool first)
    {
        if (target is not TextBlock text) return;
        text.TextHighlighters.Clear();
        if (value is not FieldComparison row) { text.Text = ""; return; }
        text.Text = first ? row.Value1 : row.Value2;
        if (row.Status == FieldComparisonStatus.Match || row.Message1 == row.Message2) return;
        var start = 0;
        var length = text.Text.Length;
        if (row.Message1 is { Length: > 0 } a && row.Message2 is { Length: > 0 } b)
        {
            var left = StringInfo.ParseCombiningCharacters(a);
            var right = StringInfo.ParseCombiningCharacters(b);
            string Element(string input, int[] offsets, int i) => input[offsets[i]..(i + 1 < offsets.Length ? offsets[i + 1] : input.Length)];
            var prefix = 0;
            while (prefix < left.Length && prefix < right.Length && Element(a, left, prefix) == Element(b, right, prefix)) prefix++;
            var suffix = 0;
            while (suffix < left.Length - prefix && suffix < right.Length - prefix &&
                Element(a, left, left.Length - suffix - 1) == Element(b, right, right.Length - suffix - 1)) suffix++;
            var offsets = first ? left : right;
            start = prefix < offsets.Length ? offsets[prefix] : text.Text.Length;
            var end = suffix > 0 ? offsets[offsets.Length - suffix] : text.Text.Length;
            length = end - start;
        }
        if (length == 0) return;
        var highlight = new TextHighlighter
        {
            Background = new SolidColorBrush(first ? Windows.UI.Color.FromArgb(255, 255, 208, 208) : Windows.UI.Color.FromArgb(255, 185, 241, 199)),
            Foreground = new SolidColorBrush(Colors.Black)
        };
        highlight.Ranges.Add(new TextRange { StartIndex = start, Length = length });
        text.TextHighlighters.Add(highlight);
    }
}
