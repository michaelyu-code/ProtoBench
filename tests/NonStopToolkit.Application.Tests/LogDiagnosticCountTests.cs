using NonStopToolkit.Core.Diagnostics;
using NonStopToolkit.Infrastructure.Logs;

namespace NonStopToolkit.Application.Tests;

public sealed class LogDiagnosticCountTests
{
    [Fact]
    public void SummaryCountsAllSeveritiesEvenWhenDetailsAreCapped()
    {
        using var store = new LogStore();
        for (var i = 0; i < 1250; i++) store.AddDiagnostic(new("test.warning", "Warning", DiagnosticSeverity.Warning));
        store.AddDiagnostic(new("test.error", "Error", DiagnosticSeverity.Error));
        store.AddDiagnostic(new("test.info", "Notice", DiagnosticSeverity.Information));
        Assert.Equal(1000, store.Diagnostics.Count);
        Assert.Equal(1250, store.DiagnosticWarnings);
        Assert.Equal(1, store.DiagnosticErrors);
        Assert.Equal(1, store.DiagnosticInformation);
    }
}
