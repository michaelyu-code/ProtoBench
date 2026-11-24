using ProtoBench.Desktop.Features.Text;
using ProtoBench.Desktop.Services;
using ProtoBench.Parsing;

namespace ProtoBench.Application.Tests;

public sealed class ComparisonReportTests
{
    [Fact]
    public async Task TextReportIncludesHighlightedChangesEscapesMarkupAndRejectsStaleResults()
    {
        using var vm = new TextCompareWorkspace();
        Assert.Throws<InvalidOperationException>(() => vm.CreateDifferencesReport());
        vm.Original = "<script>alert('x')</script>\r\nold";
        vm.Modified = "<script>alert('x')</script>\r\nnew";
        await vm.CompareCommand.ExecuteAsync(null);
        var report = vm.CreateDifferencesReport();
        Assert.Contains("&lt;script&gt;", report);
        Assert.DoesNotContain("<script>", report);
        Assert.Contains("<del>old</del>", report);
        Assert.Contains("<ins>new</ins>", report);
        vm.Modified = "edited";
        Assert.Throws<InvalidOperationException>(() => vm.CreateDifferencesReport());
    }

    [Fact]
    public async Task IdenticalAndEmptyTextsCanBeExported()
    {
        using var vm = new TextCompareWorkspace();
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Contains("Texts are identical.", vm.CreateDifferencesReport());
        vm.Original = vm.Modified = "same\ntext";
        await vm.CompareCommand.ExecuteAsync(null);
        Assert.Contains("same\ntext", vm.CreateDifferencesReport());
        Assert.DoesNotContain("<del>", vm.CreateDifferencesReport());
    }

    [Fact]
    public void MessageReportPreservesMissingEmptyMultilineAndUntrustedValues()
    {
        var report = ComparisonReport.Message("Summary", "<spec>", "Filter scope", [
            new("Reply / 55 / tag", "<field>", 1, null, "", FieldComparisonStatus.MissingFromMessage1),
            new("value", "Value", 0, "<img src=x onerror=alert(1)>", "a\nb\t&", FieldComparisonStatus.Changed)
        ], "<diagnostic>");
        Assert.Contains("Reply / 55 / tag", report);
        Assert.Contains("Missing in message 1", report);
        Assert.Contains("(missing)", report);
        Assert.Contains("(empty)", report);
        Assert.Contains("a\nb\t&amp;", report);
        Assert.Contains("&lt;img", report);
        Assert.DoesNotContain("<img", report);
        Assert.Contains("&lt;spec&gt;", report);
        Assert.Contains("&lt;diagnostic&gt;", report);
    }
}
