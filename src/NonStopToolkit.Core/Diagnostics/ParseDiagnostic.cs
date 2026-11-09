namespace NonStopToolkit.Core.Diagnostics;

public enum DiagnosticSeverity
{
    Information, Warning, Error
}
public sealed record ParseDiagnostic(string Code, string Message, DiagnosticSeverity Severity = DiagnosticSeverity.Error, long? Line = null);
