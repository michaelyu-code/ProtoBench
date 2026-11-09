using ProtoBench.Infrastructure.Updates;
using ProtoBench.Infrastructure.Logging;
using ProtoBench.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace ProtoBench.Desktop;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var failures = new ProcessExceptionLogging(ApplicationLogging.Logger);
        try { StartApplication(args); }
        catch (Exception ex)
        {
            ApplicationLogging.Logger.LogCritical(new EventId(106), ex, "Process bootstrap failed");
            throw;
        }
    }
    private static void StartApplication(string[] args)
    {
        var options = UpdateOptions.FromAssembly(typeof(Program).Assembly);
        if (options.Enabled)
            Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
