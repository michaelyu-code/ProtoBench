using ProtoBench.Core.Documents;

namespace ProtoBench.Application.Services;

public static class FieldCheckReport
{
    public static string Format(IEnumerable<FieldCheck> checks, IEnumerable<FieldFinding> findings)
    {
        var details = checks.Select(c => $"{c.Check} — {c.Outcome}\n{c.Detail}\nRule: {c.RuleId}").ToArray();
        var reported = findings.Select(f => $"{f.Status}: {f.Explanation}\nRule: {f.RuleId}").ToArray();
        var sections = new List<string>();
        sections.Add(details.Length > 0 ? string.Join("\n\n", details)
            : "This parser supplies findings only; details of successful checks were not reported.");
        sections.Add(reported.Length > 0 ? "Reported findings\n" + string.Join("\n\n", reported) : "Reported findings: none.");
        return string.Join("\n\n", sections);
    }
}
