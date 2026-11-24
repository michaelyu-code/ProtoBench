namespace ProtoBench.Utilities.Text;

public sealed record TextCompareOptions(bool IgnoreCase = false, bool IgnoreWhitespace = false, bool IgnoreBlankLines = false)
{
    public int ActiveCount => (IgnoreCase ? 1 : 0) + (IgnoreWhitespace ? 1 : 0) + (IgnoreBlankLines ? 1 : 0);
}

public sealed record ComparisonLine(int Number, int Start, string Text)
{
    public int End => Start + Text.Length;
    public string Content => Text.TrimEnd('\r', '\n', '\f', '\u0085', '\u2028', '\u2029');
}

public sealed record ComparisonRow(ComparisonLine? Left, ComparisonLine? Right, bool IsChange, int ChangeIndex);
public sealed record ComparisonChange(int Index, int FirstRow, int RowCount, int LeftStart, int LeftLength, int RightStart, int RightLength);

/// <summary>Alignment and source ranges. Display spacers never become input characters.</summary>
public sealed record TextCompareDocument(IReadOnlyList<ComparisonRow> Rows, IReadOnlyList<ComparisonChange> Changes, bool IsExactMatch, bool IsCoarse)
{
    public static TextCompareDocument Create(string left, string right, TextCompareOptions options, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var a = Split(left);
        var b = Split(right);
        var aa = a.Where(l => !options.IgnoreBlankLines || !string.IsNullOrWhiteSpace(l.Content)).ToArray();
        var bb = b.Where(l => !options.IgnoreBlankLines || !string.IsNullOrWhiteSpace(l.Content)).ToArray();
        string Key(ComparisonLine line)
        {
            token.ThrowIfCancellationRequested();
            var text = line.Content;
            if (options.IgnoreWhitespace) text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            if (options.IgnoreCase) text = text.ToUpperInvariant();
            return text + line.Text[line.Content.Length..];
        }
        var ak = aa.Select(Key).ToArray();
        var bk = bb.Select(Key).ToArray();
        var edits = Align(ak, bk, token, out var coarse);
        var rows = new List<ComparisonRow>();
        var ai = 0; var bi = 0; var ap = 0; var bp = 0;
        for (var e = 0; e < edits.Count;)
        {
            token.ThrowIfCancellationRequested();
            if (edits[e] == 0)
            {
                AddIgnored(aa[ai].Number - 1, bb[bi].Number - 1);
                rows.Add(new(aa[ai++], bb[bi++], false, -1));
                ap++; bp++; e++;
                continue;
            }
            var removed = new List<ComparisonLine>();
            var added = new List<ComparisonLine>();
            while (e < edits.Count && edits[e] != 0)
            {
                if (edits[e++] < 0) removed.Add(aa[ai++]);
                else added.Add(bb[bi++]);
            }
            for (var i = 0; i < Math.Max(removed.Count, added.Count); i++)
            {
                var al = i < removed.Count ? removed[i] : null;
                var bl = i < added.Count ? added[i] : null;
                AddIgnored(al is null ? ap : al.Number - 1, bl is null ? bp : bl.Number - 1);
                rows.Add(new(al, bl, true, -1));
                if (al is not null) ap++;
                if (bl is not null) bp++;
            }
        }
        AddIgnored(a.Length, b.Length);
        var changes = new List<ComparisonChange>();
        var leftOffset = 0; var rightOffset = 0;
        for (var i = 0; i < rows.Count;)
        {
            if (!rows[i].IsChange)
            {
                leftOffset += rows[i].Left?.Text.Length ?? 0;
                rightOffset += rows[i++].Right?.Text.Length ?? 0;
                continue;
            }
            var start = i; var ls = leftOffset; var rs = rightOffset;
            do
            {
                rows[i] = rows[i] with { ChangeIndex = changes.Count };
                leftOffset += rows[i].Left?.Text.Length ?? 0;
                rightOffset += rows[i++].Right?.Text.Length ?? 0;
            } while (i < rows.Count && rows[i].IsChange);
            changes.Add(new(changes.Count, start, i - start, ls, leftOffset - ls, rs, rightOffset - rs));
        }
        return new(rows.AsReadOnly(), changes.AsReadOnly(), left == right, coarse);

        void AddIgnored(int untilA, int untilB)
        {
            while (ap < untilA || bp < untilB)
            {
                if (rows.Count % 256 == 0) token.ThrowIfCancellationRequested();
                rows.Add(new(ap < untilA ? a[ap++] : null, bp < untilB ? b[bp++] : null, false, -1));
            }
        }
    }

    private static ComparisonLine[] Split(string text)
    {
        var lines = new List<ComparisonLine>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n' or '\f' or '\u0085' or '\u2028' or '\u2029')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            lines.Add(new(lines.Count + 1, start, text[start..(i + 1)]));
            start = i + 1;
        }
        if (start < text.Length) lines.Add(new(lines.Count + 1, start, text[start..]));
        return lines.ToArray();
    }

    // Bounded Myers alignment, retaining every line if refinement exceeds the work budget.
    private static List<int> Align(string[] a, string[] b, CancellationToken token, out bool coarse)
    {
        coarse = false;
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) { token.ThrowIfCancellationRequested(); prefix++; }
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;
        var n = a.Length - prefix - suffix; var m = b.Length - prefix - suffix;
        var result = Enumerable.Repeat(0, prefix).ToList();
        if (n == 0 || m == 0)
        {
            result.AddRange(Enumerable.Repeat(-1, n));
            result.AddRange(Enumerable.Repeat(1, m));
        }
        else
        {
            var limit = Math.Min(512, n + m); var offset = limit + 1;
            var frontier = new int[2 * limit + 3]; var trace = new List<int[]>();
            var distance = -1; var budget = 8_000_000;
            for (var d = 0; d <= limit && distance < 0 && budget > 0; d++)
            {
                token.ThrowIfCancellationRequested();
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
                result.AddRange(Enumerable.Repeat(-1, n)); result.AddRange(Enumerable.Repeat(1, m));
            }
            else
            {
                var reversed = new List<int>(); var x = n; var y = m;
                for (var d = distance; d >= 0; d--)
                {
                    var previous = trace[d]; var k = x - y;
                    var pk = k == -d || k != d && previous[offset + k - 1] < previous[offset + k + 1] ? k + 1 : k - 1;
                    var px = previous[offset + pk]; var py = px - pk;
                    while (x > px && y > py) { reversed.Add(0); x--; y--; }
                    if (d == 0) break;
                    if (x == px) { reversed.Add(1); y--; } else { reversed.Add(-1); x--; }
                }
                reversed.Reverse(); result.AddRange(reversed);
            }
        }
        result.AddRange(Enumerable.Repeat(0, suffix));
        return result;
    }
}
