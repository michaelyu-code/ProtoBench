using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Text;

internal readonly record struct TextCompareSpace(ComparisonLine Line, double Pixels);

/// <summary>Unwrapped paragraphs have uniform heights; only gaps need native formatting.</summary>
internal sealed record TextCompareSpacing(IReadOnlyList<TextCompareSpace> Left, IReadOnlyList<TextCompareSpace> Right, double Height)
{
    public static double TailSpace(double height, double endTop, double origin, double lineHeight) =>
        Math.Max(0, height - (endTop - origin) - lineHeight);

    public static TextCompareSpacing Create(TextCompareDocument document, double leftHeight, double rightHeight)
    {
        var left = new List<TextCompareSpace>();
        var right = new List<TextCompareSpace>();
        var y = 0d; var leftY = 0d; var rightY = 0d;
        foreach (var row in document.Rows)
        {
            if (row.Left is { } a)
            {
                if (y - leftY > .5) left.Add(new(a, y - leftY));
                leftY = y + leftHeight;
            }
            if (row.Right is { } b)
            {
                if (y - rightY > .5) right.Add(new(b, y - rightY));
                rightY = y + rightHeight;
            }
            y += Math.Max(row.Left is null ? 0 : leftHeight, row.Right is null ? 0 : rightHeight);
        }
        return new(left, right, y);
    }
}
