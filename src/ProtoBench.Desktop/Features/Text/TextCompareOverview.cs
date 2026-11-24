namespace ProtoBench.Desktop.Features.Text;

internal readonly record struct TextCompareOverviewMark(int ChangeIndex, double Top, double Height);

/// <summary>Maps displayed rows to overview marks using the same geometry for drawing and navigation.</summary>
internal static class TextCompareOverview
{
    public static IReadOnlyList<TextCompareOverviewMark> Create(IReadOnlyList<(int ChangeIndex, double Height)> rows, double viewportHeight)
    {
        if (!double.IsFinite(viewportHeight) || viewportHeight <= 0 || rows.Count == 0) return [];
        var total = rows.Sum(r => Math.Max(1, r.Height));
        var scale = Math.Min(1, viewportHeight / total);
        var marks = new List<TextCompareOverviewMark>();
        var offset = 0d;
        for (var i = 0; i < rows.Count;)
        {
            var change = rows[i].ChangeIndex;
            var top = offset;
            do { offset += Math.Max(1, rows[i++].Height); }
            while (i < rows.Count && rows[i].ChangeIndex == change);
            if (change < 0) continue;
            var height = Math.Min(viewportHeight, Math.Max(3, (offset - top) * scale));
            marks.Add(new(change, Math.Clamp(top * scale, 0, viewportHeight - height), height));
        }
        return marks;
    }

    public static int FindNearest(IReadOnlyList<TextCompareOverviewMark> marks, double y) => marks.Count == 0 ? -1 :
        marks.MinBy(mark => y < mark.Top ? mark.Top - y : Math.Max(0, y - mark.Top - mark.Height)).ChangeIndex;
}
