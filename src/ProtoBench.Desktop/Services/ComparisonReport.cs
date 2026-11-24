using System.Net;
using System.Text;
using ProtoBench.Parsing;
using ProtoBench.Utilities.Text;

namespace ProtoBench.Desktop.Services;

public static class ComparisonReport
{
    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    public static string Message(string summary, string spec, string scope, IEnumerable<FieldComparison> rows, string diagnostics)
    {
        var report = Start("Message Compare differences report", summary);
        report.Append($"<p>Spec: {Encode(spec)}</p><p>{Encode(scope)}</p>");
        report.Append("<table><thead><tr><th>Field path</th><th>Name</th><th>Status</th><th>Message 1</th><th>Message 2</th></tr></thead><tbody>");
        var count = 0;
        foreach (var row in rows)
        {
            count++;
            var style = row.Status == FieldComparisonStatus.Match ? "" : row.Status == FieldComparisonStatus.Changed ? "changed" : "missing";
            report.Append($"<tr class=\"{style}\"><td>{Encode(row.Path)}</td><td>{Encode(row.Name)}</td><td>{Encode(row.StatusText)}</td><td><pre>{Encode(row.Value1)}</pre></td><td><pre>{Encode(row.Value2)}</pre></td></tr>");
        }
        report.Append("</tbody></table>");
        if (count == 0) report.Append("<p>No rows match the current filters.</p>");
        if (diagnostics.Length > 0) report.Append($"<h2>Parser diagnostics</h2><pre>{Encode(diagnostics)}</pre>");
        return report.Append("</body></html>").ToString();
    }

    public static string Text(string summary, TextComparisonResult result, TextCompareDocument? alignment = null, TextCompareOptions? options = null)
    {
        var report = Start("Text Compare differences report", summary);
        if (alignment is not null && options is { ActiveCount: > 0 })
        {
            var rules = new List<string>();
            if (options.IgnoreCase) rules.Add("ignore case");
            if (options.IgnoreWhitespace) rules.Add("ignore whitespace");
            if (options.IgnoreBlankLines) rules.Add("ignore blank lines");
            report.Append($"<p>Rules: {Encode(string.Join(", ", rules))}. Ignored differences are shown without highlighting. Both original inputs are retained below.</p>");
            report.Append("<table><thead><tr><th>Original line</th><th>Original</th><th>Modified line</th><th>Modified</th></tr></thead><tbody>");
            foreach (var row in alignment.Rows)
            {
                report.Append($"<tr><td>{row.Left?.Number}</td><td><pre>");
                if (row.IsChange && row.Left is not null) report.Append("<del>");
                report.Append(Encode(row.Left?.Text ?? ""));
                if (row.IsChange && row.Left is not null) report.Append("</del>");
                report.Append($"</pre></td><td>{row.Right?.Number}</td><td><pre>");
                if (row.IsChange && row.Right is not null) report.Append("<ins>");
                report.Append(Encode(row.Right?.Text ?? ""));
                if (row.IsChange && row.Right is not null) report.Append("</ins>");
                report.Append("</pre></td></tr>");
            }
            return report.Append("</tbody></table></body></html>").ToString();
        }
        report.Append("<p>Removed text is red and struck through. Added text is green and underlined. Unchanged text provides context.</p><pre class=\"diff\">");
        foreach (var difference in result.Differences)
        {
            var text = Encode(difference.Text);
            report.Append(difference.Kind switch
            {
                TextDifferenceKind.Removed => $"<del>{text}</del>",
                TextDifferenceKind.Added => $"<ins>{text}</ins>",
                _ => text
            });
        }
        return report.Append("</pre></body></html>").ToString();
    }

    private static StringBuilder Start(string title, string summary) => new StringBuilder("""
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
        <title>
        """).Append(Encode(title)).Append("""
        </title><style>
        body{font:15px system-ui,sans-serif;color:#202020;background:white;margin:2rem}
        table{border-collapse:collapse;width:100%}th,td{border:1px solid #bbb;padding:.5rem;text-align:left;vertical-align:top}
        pre{white-space:pre-wrap;overflow-wrap:anywhere;tab-size:4;margin:0;font:14px Consolas,monospace}
        .diff{border:1px solid #bbb;padding:1rem}.changed{background:#fff3cd}.missing{background:#f1e4ff}
        del{background:#ffd2d2;text-decoration:line-through}ins{background:#c4f0cd;text-decoration:underline}
        </style></head><body><h1>
        """).Append(Encode(title)).Append("</h1><p>").Append(Encode(summary)).Append("</p>");
}
