using System.Reflection;
using System.Runtime.Loader;

namespace ProtoBench.Infrastructure.Plugins;

internal sealed class PluginLoadContext(string assemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver resolver = new(assemblyPath);
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "ProtoBench.PluginSdk", "ProtoBench.PluginSdk.WinUI", "ProtoBench.Core",
        "ProtoBench.Parsing", "ProtoBench.Generation", "Microsoft.WinUI", "WinRT.Runtime",
        "Microsoft.Windows.SDK.NET", "Microsoft.InteractiveExperiences"
    };

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Contract type identity must be shared with the host, even if a package contains SDK copies.
        if (assemblyName.Name is { } name && Shared.Contains(name))
            return Default.LoadFromAssemblyName(assemblyName);
        var path = resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }
    protected override nint LoadUnmanagedDll(string name)
    {
        var path = resolver.ResolveUnmanagedDllToPath(name);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
