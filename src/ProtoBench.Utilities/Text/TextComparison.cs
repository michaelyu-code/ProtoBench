using System.Globalization;
using System.Text;

namespace ProtoBench.Utilities.Text;

public enum TextDifferenceKind { Equal, Removed, Added }
public sealed record TextDifference(TextDifferenceKind Kind, string Text);
public sealed record TextComparisonResult(IReadOnlyList<TextDifference> Differences, bool HasCoarseChanges)
{
    public bool AreEqual => Differences.All(d => d.Kind == TextDifferenceKind.Equal);
}

public static class TextComparison
{
    private const int MaxRefinementCharacters = 200_000;
    private const int MaxDistance = 512;

    public static TextComparisonResult Compare(string original, string modified, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(modified);
        cancellationToken.ThrowIfCancellationRequested();
        var budget = 8_000_000;
        var coarse = false;
        var lines = Diff(Lines(original), Lines(modified));
        var result = new List<TextDifference>();
        for (var i = 0; i < lines.Count;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lines[i].Kind == TextDifferenceKind.Equal) { result.Add(lines[i++]); continue; }
            var before = new StringBuilder();
            var after = new StringBuilder();
            while (i < lines.Count && lines[i].Kind != TextDifferenceKind.Equal)
            {
                var edit = lines[i++];
                (edit.Kind == TextDifferenceKind.Removed ? before : after).Append(edit.Text);
            }
            // Retain all input, but avoid allocating one string per grapheme for huge
            // changed regions. Large replacements can be highlighted as whole blocks.
            if (before.Length == 0 || after.Length == 0 || (long)before.Length + after.Length > MaxRefinementCharacters)
            {
                if (before.Length > 0 && after.Length > 0) coarse = true;
                if (before.Length > 0) result.Add(new(TextDifferenceKind.Removed, before.ToString()));
                if (after.Length > 0) result.Add(new(TextDifferenceKind.Added, after.ToString()));
            }
            else result.AddRange(Diff(Elements(before.ToString()), Elements(after.ToString())));
        }
        return new(result.AsReadOnly(), coarse);

        List<TextDifference> Diff(string[] a, string[] b)
        {
            var prefix = 0;
            while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
            var suffix = 0;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
            var n = a.Length - prefix - suffix;
            var m = b.Length - prefix - suffix;
            var edits = new List<TextDifference>();
            Add(TextDifferenceKind.Equal, string.Concat(a.Take(prefix)));
            if (n == 0 || m == 0)
            {
                Add(TextDifferenceKind.Removed, string.Concat(a.Skip(prefix).Take(n)));
                Add(TextDifferenceKind.Added, string.Concat(b.Skip(prefix).Take(m)));
            }
            else
            {
                var limit = Math.Min(MaxDistance, n + m);
                var offset = limit + 1;
                var frontier = new int[2 * limit + 3];
                var trace = new List<int[]>();
                var distance = -1;
                for (var d = 0; d <= limit && distance < 0 && budget > 0; d++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    trace.Add((int[])frontier.Clone());
                    for (var k = -d; k <= d && budget-- > 0; k += 2)
                    {
                        var x = k == -d || k != d && frontier[offset + k - 1] < frontier[offset + k + 1]
                            ? frontier[offset + k + 1] : frontier[offset + k - 1] + 1;
                        var y = x - k;
                        while (x < n && y < m && a[prefix + x] == b[prefix + y] && budget-- > 0) { x++; y++; }
                        frontier[offset + k] = x;
                        if (x >= n && y >= m) { distance = d; break; }
                    }
                }
                if (distance < 0)
                {
                    coarse = true;
                    Add(TextDifferenceKind.Removed, string.Concat(a.Skip(prefix).Take(n)));
                    Add(TextDifferenceKind.Added, string.Concat(b.Skip(prefix).Take(m)));
                }
                else
                {
                    var reversed = new List<TextDifference>();
                    var x = n;
                    var y = m;
                    for (var d = distance; d >= 0; d--)
                    {
                        var previous = trace[d];
                        var k = x - y;
                        var previousK = k == -d || k != d && previous[offset + k - 1] < previous[offset + k + 1] ? k + 1 : k - 1;
                        var previousX = previous[offset + previousK];
                        var previousY = previousX - previousK;
                        while (x > previousX && y > previousY)
                        {
                            reversed.Add(new(TextDifferenceKind.Equal, a[prefix + --x]));
                            y--;
                        }
                        if (d == 0) break;
                        if (x == previousX) reversed.Add(new(TextDifferenceKind.Added, b[prefix + --y]));
                        else reversed.Add(new(TextDifferenceKind.Removed, a[prefix + --x]));
                    }
                    reversed.Reverse();
                    // Group once to avoid quadratic concatenation of long unchanged spans.
                    for (var i = 0; i < reversed.Count;)
                    {
                        var kind = reversed[i].Kind;
                        var text = new StringBuilder();
                        do { text.Append(reversed[i++].Text); } while (i < reversed.Count && reversed[i].Kind == kind);
                        Add(kind, text.ToString());
                    }
                }
            }
            Add(TextDifferenceKind.Equal, string.Concat(a.Skip(a.Length - suffix)));
            return edits;

            void Add(TextDifferenceKind kind, string text) { if (text.Length > 0) edits.Add(new(kind, text)); }
        }
    }

    private static string[] Elements(string text)
    {
        var elements = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());
        return elements.ToArray();
    }

    private static string[] Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines.ToArray();
    }
}
