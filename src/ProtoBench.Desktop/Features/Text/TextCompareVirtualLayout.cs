using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Features.Text;

internal readonly record struct TextCompareLineSpace(int Line, double Height, bool Above);
internal sealed record TextCompareVirtualLayout(IReadOnlyList<TextCompareLineSpace> Left, IReadOnlyList<TextCompareLineSpace> Right)
{
    public static TextCompareVirtualLayout Create(TextCompareDocument document, IReadOnlyList<double> leftHeights, IReadOnlyList<double> rightHeights)
    {
        var left = new Dictionary<(int Line, bool Above), double>(); var right = new Dictionary<(int Line, bool Above), double>();
        var previousLeft = 0; var previousRight = 0;
        foreach (var row in document.Rows)
        {
            var a = Height(row.Left, leftHeights); var b = Height(row.Right, rightHeights); var height = Math.Max(a, b);
            if (row.Left is { } l) previousLeft = l.Number;
            if (row.Right is { } r) previousRight = r.Number;
            Add(left, previousLeft, height - a); Add(right, previousRight, height - b);
        }
        // An empty editor still has a caret line, but no source row to align.
        // Keep that line at the top so the next paste starts in a visible input.
        if (previousLeft == 0) left.Clear();
        if (previousRight == 0) right.Clear();
        return new(left.Select(p => new TextCompareLineSpace(p.Key.Line, p.Value, p.Key.Above)).ToArray(),
            right.Select(p => new TextCompareLineSpace(p.Key.Line, p.Value, p.Key.Above)).ToArray());

        static double Height(ComparisonLine? line, IReadOnlyList<double> heights) => line is null ? 0 :
            line.Number <= heights.Count ? Math.Max(1, heights[line.Number - 1]) : 22;
        static void Add(Dictionary<(int Line, bool Above), double> target, int previous, double height)
        {
            if (height <= .5) return;
            var key = (Math.Max(1, previous), previous == 0);
            target[key] = target.GetValueOrDefault(key) + height;
        }
    }
}
