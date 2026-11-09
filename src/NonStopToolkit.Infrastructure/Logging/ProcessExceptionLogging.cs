using Microsoft.Extensions.Logging;

namespace NonStopToolkit.Infrastructure.Logging;

/// <summary>Best-effort logging; does not suppress fatal exceptions or change task exception policy.</summary>
public sealed class ProcessExceptionLogging : IDisposable
{
    private readonly ILogger logger;
    public ProcessExceptionLogging(ILogger logger)
    {
        this.logger = logger;
        AppDomain.CurrentDomain.UnhandledException += DomainFailure;
        TaskScheduler.UnobservedTaskException += TaskFailure;
    }
    private void DomainFailure(object sender, UnhandledExceptionEventArgs args) =>
        logger.LogCritical(new EventId(103), args.ExceptionObject as Exception, "Unhandled managed exception");
    private void TaskFailure(object? sender, UnobservedTaskExceptionEventArgs args) =>
        logger.LogError(new EventId(104), args.Exception, "Unobserved task exception");
    public void Dispose()
    {
        AppDomain.CurrentDomain.UnhandledException -= DomainFailure;
        TaskScheduler.UnobservedTaskException -= TaskFailure;
    }
}
