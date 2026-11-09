using Microsoft.Extensions.Logging;
using NonStopToolkit.Infrastructure.Configuration;
using NonStopToolkit.Infrastructure.Logging;

namespace NonStopToolkit.Desktop.Services;

internal static class ApplicationLogging
{
    internal static readonly LocalFileLoggerProvider Provider = new(AppPaths.LogDirectory);
    internal static readonly ILogger Logger = Provider.CreateLogger("NonStopToolkit.Desktop.App");
}
