using ProtoBench.Application.Services;
using ProtoBench.Core.Documents;

namespace ProtoBench.Application.Tests;

public sealed class FieldCheckReportTests
{
    [Fact]
    public void ReportIncludesCheckTypesOutcomesAndOriginalRuleIds()
    {
        var text = FieldCheckReport.Format([new("11", "Exact length", "Passed", "Expected 6 characters; found 6.", "field.format.length")],
            [new("11", "Review", "Plugin needs transaction context.", "plugin.trace.context")]);
        Assert.Contains("Exact length — Passed", text);
        Assert.Contains("Rule: field.format.length", text);
        Assert.Contains("Rule: plugin.trace.context", text);
    }

    [Fact]
    public void FindingsOnlyParserNeverClaimsSuccessfulChecksWerePerformed()
    {
        var text = FieldCheckReport.Format([], []);
        Assert.Contains("successful checks were not reported", text);
        Assert.DoesNotContain("Passed", text);
    }
}
