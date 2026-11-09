using Microsoft.Extensions.Logging;
using ProtoBench.Infrastructure.Configuration;
using ProtoBench.Infrastructure.Logging;

namespace ProtoBench.Desktop.Services;

internal static class ApplicationLogging
{
    internal static readonly LocalFileLoggerProvider Provider = new(AppPaths.LogDirectory);
    internal static readonly ILogger Logger = Provider.CreateLogger("ProtoBench.Desktop.App");
}
