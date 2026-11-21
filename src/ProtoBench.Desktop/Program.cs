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
        if (LocalDataRemoval.TryRun(args)) return;
        var options = UpdateOptions.FromAssembly(typeof(Program).Assembly);
        var bootstrapInstaller = options.Enabled;
#if PROTOBENCH_INSTALLER
        // Installation lifecycle hooks must run before logging or WinUI initialization.
        bootstrapInstaller = true;
#endif
        if (bootstrapInstaller)
            Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var failures = new ProcessExceptionLogging(ApplicationLogging.Logger);
        try { StartApplication(); }
        catch (Exception ex)
        {
            ApplicationLogging.Logger.LogCritical(new EventId(106), ex, "Process bootstrap failed");
            throw;
        }
    }
    private static void StartApplication()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
