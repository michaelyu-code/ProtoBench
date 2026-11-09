using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using NonStopToolkit.Application.Tools;
using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Core.Documents;
using NonStopToolkit.Desktop.Services;
using NonStopToolkit.Desktop.Features.Converters;
using NonStopToolkit.Desktop.Features.Parsers;
using NonStopToolkit.Desktop.Features.Generators;
using NonStopToolkit.Desktop.Features.Payments;
using NonStopToolkit.Desktop.Features.Calculators;
using NonStopToolkit.Desktop.Features.Plugins;
using NonStopToolkit.Infrastructure.Plugins;
using NonStopToolkit.Desktop.Shell;
using NonStopToolkit.Infrastructure.Configuration;
using NonStopToolkit.Infrastructure.Files;
using NonStopToolkit.Infrastructure.Logging;
using NonStopToolkit.Infrastructure.Updates;
using NonStopToolkit.Parsing.Definitions;
using NonStopToolkit.Parsing.Engines;
using NonStopToolkit.Parsing.Registry;
using NonStopToolkit.Generation.Definitions;
using NonStopToolkit.Generation.Engines;
using NonStopToolkit.Generation.Registry;
using NonStopToolkit.Utilities.Numeric;
using NonStopToolkit.Utilities.Text;
namespace NonStopToolkit.Desktop;

public partial class App : Microsoft.UI.Xaml.Application
{
    private ServiceProvider? services;
    private MainWindow? window;
    public App()
    {
        UnhandledException += (_, args) =>
        {
            ApplicationLogging.Logger.LogCritical(new EventId(102), args.Exception, "Unhandled UI error");
        };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try { LaunchApplication(args); }
        catch (Exception ex)
        {
            ApplicationLogging.Logger.LogCritical(new EventId(105), ex, "Application startup failed");
            throw;
        }
    }
    private void LaunchApplication(LaunchActivatedEventArgs args)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "definitions");
        var user = Path.Combine(AppPaths.UserDirectory, "definitions");
        Directory.CreateDirectory(Path.Combine(user, "parsers"));
        Directory.CreateDirectory(Path.Combine(user, "generators"));
        Directory.CreateDirectory(Path.Combine(user, "messages"));
        // Parser specs come from extensions or explicit user definitions, never app defaults.
        var parsers = DefinitionLoader.Load(new[] { Path.Combine(user, "parsers") }, ParserDefinition.FromJson, d => d.Id);
        var generators = DefinitionLoader.Load(new[] { Path.Combine(bundled, "generators"), Path.Combine(user, "generators") }, GeneratorDefinition.FromJson, d => d.Id);
        var messages = DefinitionLoader.Load(new[] { Path.Combine(user, "messages") }, MessageProfile.FromJson, d => d.Id);
        var collection = new ServiceCollection();
        var updateOptions = UpdateOptions.FromAssembly(typeof(App).Assembly);
        collection.AddSingleton(_ => new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 65536 });
        collection.AddSingleton(sp => new UpdateService(updateOptions,
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion ?? "1.0.0",
            sp.GetRequiredService<System.Net.Http.HttpClient>(), () => new VelopackUpdater(updateOptions.FeedUrl)));
        collection.AddLogging(b => b.AddProvider(ApplicationLogging.Provider));
        collection.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        collection.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(Path.Combine(AppPaths.UserDirectory, "settings.json"), sp.GetRequiredService<IAtomicFileWriter>()));
        collection.AddSingleton<IBaseConverter, BaseConverter>();
        collection.AddSingleton<IAsciiHexConverter, AsciiHexConverter>();
        collection.AddSingleton<ICp500Converter, Cp500Converter>();
        collection.AddSingleton<SessionSettings>();
        collection.AddSingleton<FileDialogService>();
        collection.AddSingleton(new ToolDescriptor("base-converter", "Base Converter", "Convert numeric bases, ASCII and EBCDIC CP500 text and hex bytes.", ToolCategory.Converters, "\uE8EF"));
        collection.AddSingleton(new ToolDescriptor("log-parser", "Log Parser", "Detect and inspect log formats supplied by extensions or custom definitions.", ToolCategory.Parsers, "\uE8A5"));
        collection.AddSingleton(new ToolDescriptor("file-generator", "File Generator", "Validate, preview and save fixed-width records.", ToolCategory.Generators, "\uE8A5"));
        collection.AddSingleton(new ToolDescriptor("emv-decoder", "EMV Tag Decoder", "13 EMV tags, scheme profiles, Form Factor / Third Party Data, CVM, TTQ, CTQ and BER-TLV.", ToolCategory.Payments, "\uE8EC"));
        collection.AddSingleton(new ToolDescriptor("iso8583-bitmap", "ISO 8583 Bitmap", "Decode hex/binary bitmaps and encode primary/secondary field lists.", ToolCategory.Payments, "\uE8A5"));
        collection.AddSingleton(new ToolDescriptor("luhn", "Luhn Check Digit", "Validate payment card checksums and calculate a Luhn check digit.", ToolCategory.Payments, "\uE8EF"));
        collection.AddSingleton(new ToolDescriptor("arqc", "ARQC Calculator", "Calculate and verify session-key application cryptograms with ISO 9797-1 algorithm 3.", ToolCategory.Payments, "\uE72E"));
        collection.AddSingleton<IToolRegistry, ToolRegistry>();
        collection.AddSingleton(new ToolDescriptor("bitwise-calculator", "Bitwise Calculator", "AND, OR, XOR, NOT, logical shifts and rotations in binary, decimal and hex.", ToolCategory.Calculators, "\uE8EF"));
        collection.AddSingleton(new ToolDescriptor("base64-calculator", "Base64", "Encode/decode UTF-8 text and hex bytes with standard or URL-safe Base64.", ToolCategory.Calculators, "\uE8EF"));
        collection.AddSingleton(new ToolDescriptor("3des-calculator", "3DES Calculator", "Encrypt/decrypt hex bytes with two-key or three-key Triple DES, ECB/CBC and explicit padding.", ToolCategory.Calculators, "\uE72E"));
        var packages = new PluginPackageManager(Path.Combine(AppPaths.UserDirectory, "plugins"));
        var plugins = PluginCatalog.Load(packages,
            collection.Where(s => s.ServiceType == typeof(ToolDescriptor)).Select(s => ((ToolDescriptor)s.ImplementationInstance!).Id),
            parsers.Definitions.Select(d => d.Id).Concat(messages.Definitions.Select(d => d.Id)), generators.Definitions.Select(d => d.Id));
        collection.AddSingleton(packages);
        // Factory registration gives DI ownership of plugin disposal after workspace shutdown.
        collection.AddSingleton(_ => plugins);
        var pluginDiagnostics = new List<string>(plugins.Diagnostics);
        var extraParsers = DefinitionLoader.Load(plugins.Plugins.Select(p => Path.Combine(p.Directory, "definitions", "parsers")), ParserDefinition.FromJson, d => d.Id);
        var extraMessages = DefinitionLoader.Load(plugins.Plugins.Select(p => Path.Combine(p.Directory, "definitions", "messages")), MessageProfile.FromJson, d => d.Id);
        var extraGenerators = DefinitionLoader.Load(plugins.Plugins.Select(p => Path.Combine(p.Directory, "definitions", "generators")), GeneratorDefinition.FromJson, d => d.Id);
        pluginDiagnostics.AddRange(extraParsers.Diagnostics.Concat(extraMessages.Diagnostics).Concat(extraGenerators.Diagnostics));
        var parserIds = new HashSet<string>(parsers.Definitions.Select(d => d.Id).Concat(messages.Definitions.Select(d => d.Id))
            .Concat(plugins.Plugins.SelectMany(p => p.Parsers).Select(p => p.FormatId)), StringComparer.OrdinalIgnoreCase);
        var generatorIds = new HashSet<string>(generators.Definitions.Select(d => d.Id)
            .Concat(plugins.Plugins.SelectMany(p => p.Generators).Select(g => g.GeneratorId)), StringComparer.OrdinalIgnoreCase);
        bool Accept(string id, HashSet<string> ids)
        {
            if (ids.Add(id)) return true;
            pluginDiagnostics.Add($"Plugin definition '{id}' conflicts with an existing format and was skipped.");
            return false;
        }
        var fieldDecoders = plugins.Plugins.SelectMany(p => p.FieldDecoders).ToArray();
        var allParsers = parsers.Definitions.Concat(extraParsers.Definitions.Where(d => Accept(d.Id, parserIds)))
            .Select(d => (NonStopToolkit.Parsing.Abstractions.ILogParser)new JsonDefinedParser(d)).ToList();
        allParsers.AddRange(messages.Definitions.Concat(extraMessages.Definitions.Where(d => Accept(d.Id, parserIds)))
            .Select(d => new RecordMessageParser(d, fieldDecoders)));
        allParsers.AddRange(plugins.Plugins.SelectMany(p => p.Parsers));
        collection.AddSingleton<IParserRegistry>(new ParserRegistry(allParsers));
        collection.AddSingleton<IGeneratorRegistry>(new GeneratorRegistry(generators.Definitions
            .Concat(extraGenerators.Definitions.Where(d => Accept(d.Id, generatorIds))).Select(d => (NonStopToolkit.Generation.Abstractions.IFileGenerator)new FixedWidthGenerator(d))
            .Concat(plugins.Plugins.SelectMany(p => p.Generators))));
        foreach (var plugin in plugins.Plugins)
        foreach (var tool in plugin.Tools)
        {
            collection.AddSingleton(new ToolDescriptor(tool.Id, tool.Name, tool.Description, ToolCategory.CustomTools, tool.Icon));
            collection.AddSingleton<IToolWorkspaceFactory>(new PluginToolWorkspaceFactory(tool));
            collection.AddSingleton<IWorkspaceViewFactory>(new PluginToolViewFactory(tool, plugin.Instance));
        }
        foreach (var kind in Enum.GetValues<CalculatorKind>())
        {
            var factory = new CalculatorWorkspaceFactory(kind);
            collection.AddSingleton<IToolWorkspaceFactory>(factory);
            collection.AddSingleton<IWorkspaceViewFactory>(new CalculatorViewFactory(factory.ToolId));
        }
        collection.AddTransient<ConverterWorkspace>();
        collection.AddTransient<ParserWorkspace>();
        collection.AddTransient<GeneratorWorkspace>();
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<ConverterWorkspace>("base-converter", sp));
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<ParserWorkspace>("log-parser", sp));
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<GeneratorWorkspace>("file-generator", sp));
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<EmvWorkspace>("emv-decoder", sp));
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<BitmapWorkspace>("iso8583-bitmap", sp));
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<LuhnWorkspace>("luhn", sp));
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<ArqcWorkspace>("arqc", sp));
        collection.AddSingleton<WorkspaceManager>();
        collection.AddSingleton<ShellViewModel>();
        collection.AddSingleton<IWorkspaceViewFactory, ConverterViewFactory>();
        collection.AddSingleton<IWorkspaceViewFactory, ParserViewFactory>();
        collection.AddSingleton<IWorkspaceViewFactory, GeneratorViewFactory>();
        collection.AddSingleton<IWorkspaceViewFactory>(new PaymentViewFactory("emv-decoder"));
        collection.AddSingleton<IWorkspaceViewFactory>(new PaymentViewFactory("iso8583-bitmap"));
        collection.AddSingleton<IWorkspaceViewFactory>(new PaymentViewFactory("luhn"));
        collection.AddSingleton<IWorkspaceViewFactory>(new PaymentViewFactory("arqc"));
        collection.AddSingleton<MainWindow>();
        services = collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        window = services.GetRequiredService<MainWindow>();
        services.GetRequiredService<FileDialogService>().Window = window;
        window.Closed += (_, _) => { services.GetRequiredService<WorkspaceManager>().Dispose(); services.Dispose(); };
        window.Activate();
        window.InitializeAsync(parsers.Diagnostics.Concat(generators.Diagnostics).Concat(messages.Diagnostics).Concat(pluginDiagnostics).ToArray());
    }
}
