using ProtoBench.Core.Networking;
using ProtoBench.Generation.Abstractions;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.PluginSdk;

namespace ProtoBench.Infrastructure.Plugins;

public sealed record LoadedPlugin(PluginManifest Manifest, string Directory, IToolkitPlugin Instance,
    IReadOnlyList<PluginTool> Tools, IReadOnlyList<ILogParser> Parsers,
    IReadOnlyList<IFileGenerator> Generators, IReadOnlyList<IFieldDecoder> FieldDecoders)
{
    public IReadOnlyList<TelnetServerProvider> TelnetServers { get; init; } = [];
}

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
        catalog.diagnostics.AddRange(packages.ApplyPendingUpdates());
        var tools = new HashSet<string>(toolIds, StringComparer.OrdinalIgnoreCase);
        var parsers = new HashSet<string>(parserIds, StringComparer.OrdinalIgnoreCase);
        var generators = new HashSet<string>(generatorIds, StringComparer.OrdinalIgnoreCase);
        var decoders = new HashSet<string>(["emvEntries"], StringComparer.Ordinal);
        var servers = new HashSet<string>(["builtin.ascii", "builtin.ansi-vt100", "builtin.telserv-tn6530"], StringComparer.OrdinalIgnoreCase);
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
                    instance.Tools.ToArray(), instance.Parsers.Select(p => (ILogParser)new PluginLogParser(p)).ToArray(), instance.Generators.ToArray(), instance.FieldDecoders.ToArray()) { TelnetServers = instance.TelnetServers.ToArray() };
                foreach (var server in plugin.TelnetServers)
                    if (string.IsNullOrWhiteSpace(server.Name) || server.Description is null || server.CreateSession is null)
                        throw new InvalidDataException("Invalid Telnet server definition.");
                ValidateIds(plugin.TelnetServers.Select(s => s.Id), servers, "Telnet server");
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
                servers.UnionWith(plugin.TelnetServers.Select(s => s.Id));
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
        // Unknown/retired menu categories remain loadable; discovery routes them to Custom Tools.
        if (tool.WebView is { } web && (string.IsNullOrWhiteSpace(web.Html) || System.Text.Encoding.UTF8.GetByteCount(web.Html) > 2_000_000))
            throw new InvalidDataException("Custom webview HTML must be nonempty and at most 2,000,000 UTF-8 bytes.");
        if (string.IsNullOrWhiteSpace(tool.Name) || tool.Description is null || tool.Help is null || tool.CreateSession is null || tool.Inputs is null || tool.Inputs.Count > 64 ||
            string.IsNullOrWhiteSpace(tool.ActionLabel) || string.IsNullOrWhiteSpace(tool.Icon))
            throw new InvalidDataException("Invalid tool definition.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in tool.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Id) || !ids.Add(input.Id) || string.IsNullOrWhiteSpace(input.Label) ||
                !Enum.IsDefined(input.Kind) || input.DefaultValue is null || input.Description is null ||
                (input.Kind == ToolInputKind.Choice && (input.Choices is not { Count: > 0 } || !input.Choices.Contains(input.DefaultValue))))
                throw new InvalidDataException("Invalid custom tool input.");
            if (input.Minimum > input.Maximum ||
                (input.Kind != ToolInputKind.Integer && (input.Minimum is not null || input.Maximum is not null)))
                throw new InvalidDataException("Invalid custom tool numeric range.");
            if (input.Kind == ToolInputKind.Integer && input.DefaultValue.Length > 0 &&
                (!int.TryParse(input.DefaultValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                 (input.Minimum is { } min && number < min) || (input.Maximum is { } max && number > max)))
                throw new InvalidDataException("Invalid custom tool numeric default.");
            if (input.Kind == ToolInputKind.FieldValues)
            {
                try { ToolInputValues.ParseFieldValues(input.DefaultValue); }
                catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException)
                { throw new InvalidDataException("Invalid supporting-field default.", ex); }
            }
        }
    }

    public void Dispose()
    {
        foreach (var plugin in plugins) { try { plugin.Instance.Dispose(); } catch { } }
        plugins.Clear();
        foreach (var context in contexts) context.Unload();
        contexts.Clear();
    }
}
