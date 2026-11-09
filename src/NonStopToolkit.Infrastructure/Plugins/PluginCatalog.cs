using NonStopToolkit.Generation.Abstractions;
using NonStopToolkit.Parsing.Abstractions;
using NonStopToolkit.PluginSdk;

namespace NonStopToolkit.Infrastructure.Plugins;

public sealed record LoadedPlugin(PluginManifest Manifest, string Directory, IToolkitPlugin Instance,
    IReadOnlyList<PluginTool> Tools, IReadOnlyList<ILogParser> Parsers,
    IReadOnlyList<IFileGenerator> Generators, IReadOnlyList<IFieldDecoder> FieldDecoders);

public sealed class PluginCatalog : IDisposable
{
    private readonly List<PluginLoadContext> contexts = [];
    private readonly List<LoadedPlugin> plugins = [];
    private readonly List<string> diagnostics = [];
    public IReadOnlyList<LoadedPlugin> Plugins => plugins;
    public IReadOnlyList<string> Diagnostics => diagnostics;

    public static PluginCatalog Load(PluginPackageManager packages, IEnumerable<string> toolIds,
        IEnumerable<string> parserIds, IEnumerable<string> generatorIds)
    {
        var catalog = new PluginCatalog();
        var tools = new HashSet<string>(toolIds, StringComparer.OrdinalIgnoreCase);
        var parsers = new HashSet<string>(parserIds, StringComparer.OrdinalIgnoreCase);
        var generators = new HashSet<string>(generatorIds, StringComparer.OrdinalIgnoreCase);
        var decoders = new HashSet<string>(["emvEntries"], StringComparer.Ordinal);
        foreach (var installed in packages.Installed())
        {
            if (installed.Error is { } error) { catalog.diagnostics.Add($"{Path.GetFileName(installed.Directory)}: {error}"); continue; }
            if (!installed.Enabled) continue;
            PluginLoadContext? context = null;
            IToolkitPlugin? instance = null;
            try
            {
                var manifest = installed.Manifest!;
                foreach (var file in manifest.Files)
                {
                    var path = PluginPackageManager.PackagePath(installed.Directory, file);
                    PluginPackageManager.RejectLinks(path);
                    if (!File.Exists(path)) throw new InvalidDataException($"Missing plugin file: {file}");
                }
                var assemblyPath = PluginPackageManager.PackagePath(installed.Directory, manifest.EntryAssembly);
                context = new PluginLoadContext(assemblyPath);
                var assembly = context.LoadFromAssemblyPath(assemblyPath);
                var type = assembly.GetType(manifest.EntryType, throwOnError: true)!;
                if (!type.IsPublic || type.IsAbstract || !typeof(IToolkitPlugin).IsAssignableFrom(type))
                    throw new InvalidDataException("Entry type must be a public IToolkitPlugin with a parameterless constructor.");
                instance = (IToolkitPlugin)Activator.CreateInstance(type)!;
                var plugin = new LoadedPlugin(manifest, installed.Directory, instance,
                    instance.Tools.ToArray(), instance.Parsers.Select(p => (ILogParser)new PluginLogParser(p)).ToArray(), instance.Generators.ToArray(), instance.FieldDecoders.ToArray());
                foreach (var tool in plugin.Tools) ValidateTool(tool);
                ValidateIds(plugin.Tools.Select(t => t.Id), tools, "tool");
                ValidateIds(plugin.Parsers.Select(p => p.FormatId), parsers, "parser");
                ValidateIds(plugin.Generators.Select(g => g.GeneratorId), generators, "generator");
                ValidateIds(plugin.FieldDecoders.Select(d => d.Id), decoders, "field decoder");
                // Commit all exports together only after every capability has passed validation.
                tools.UnionWith(plugin.Tools.Select(t => t.Id));
                parsers.UnionWith(plugin.Parsers.Select(p => p.FormatId));
                generators.UnionWith(plugin.Generators.Select(g => g.GeneratorId));
                decoders.UnionWith(plugin.FieldDecoders.Select(d => d.Id));
                catalog.plugins.Add(plugin);
                catalog.contexts.Add(context);
                instance = null;
                context = null;
            }
            catch (Exception ex)
            {
                catalog.diagnostics.Add($"{installed.Manifest!.Id}: plugin could not load ({ex.GetType().Name}). {ex.Message}");
            }
            finally
            {
                try { instance?.Dispose(); } catch { /* A failed plugin must not block other packages. */ }
                context?.Unload();
            }
        }
        return catalog;
    }

    private static void ValidateIds(IEnumerable<string> ids, HashSet<string> existing, string kind)
    {
        var seen = new HashSet<string>(existing.Comparer);
        foreach (var id in ids)
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || !seen.Add(id) || existing.Contains(id))
                throw new InvalidDataException($"Empty, duplicate or conflicting {kind} ID: {id}");
    }

    private static void ValidateTool(PluginTool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name) || tool.Description is null || tool.Help is null || tool.CreateSession is null || tool.Inputs is null || tool.Inputs.Count > 64 ||
            string.IsNullOrWhiteSpace(tool.ActionLabel) || string.IsNullOrWhiteSpace(tool.Icon))
            throw new InvalidDataException("Invalid tool definition.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in tool.Inputs)
            if (string.IsNullOrWhiteSpace(input.Id) || !ids.Add(input.Id) || string.IsNullOrWhiteSpace(input.Label) ||
                !Enum.IsDefined(input.Kind) || input.DefaultValue is null || input.Description is null ||
                (input.Kind == ToolInputKind.Choice && (input.Choices is not { Count: > 0 } || !input.Choices.Contains(input.DefaultValue))))
                throw new InvalidDataException("Invalid custom tool input.");
    }

    public void Dispose()
    {
        foreach (var plugin in plugins) { try { plugin.Instance.Dispose(); } catch { } }
        plugins.Clear();
        foreach (var context in contexts) context.Unload();
        contexts.Clear();
    }
}
