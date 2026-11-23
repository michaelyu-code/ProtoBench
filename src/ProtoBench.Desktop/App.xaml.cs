using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using ProtoBench.Application.Tools;
using ProtoBench.Application.Workspaces;
using ProtoBench.Core.Documents;
using ProtoBench.Desktop.Services;
using ProtoBench.Desktop.Features.Converters;
using ProtoBench.Desktop.Features.Parsers;
using ProtoBench.Desktop.Features.Generators;
using ProtoBench.Desktop.Features.Payments;
using ProtoBench.Desktop.Features.Calculators;
using ProtoBench.Desktop.Features.Certificates;
using ProtoBench.Desktop.Features.Networking;
using ProtoBench.Desktop.Features.Keyboard;
using ProtoBench.Desktop.Features.Text;
using ProtoBench.Application.Services;
using ProtoBench.Desktop.Features.Plugins;
using ProtoBench.Infrastructure.Plugins;
using ProtoBench.Desktop.Shell;
using ProtoBench.Infrastructure.Configuration;
using ProtoBench.Infrastructure.Files;
using ProtoBench.Infrastructure.Logging;
using ProtoBench.Infrastructure.Updates;
using ProtoBench.Parsing.Definitions;
using ProtoBench.Parsing.Engines;
using ProtoBench.Parsing.Registry;
using ProtoBench.Generation.Definitions;
using ProtoBench.Generation.Engines;
using ProtoBench.Generation.Registry;
using ProtoBench.Utilities.Numeric;
using ProtoBench.Utilities.Text;
namespace ProtoBench.Desktop;

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
        collection.AddSingleton(sp => new UpdateProxyDownloader(() => sp.GetRequiredService<SessionSettings>().Current.UpdateProxyAddress));
        collection.AddSingleton(sp => new UpdateService(updateOptions,
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion ?? "1.0.0",
            sp.GetRequiredService<System.Net.Http.HttpClient>(),
            () => new VelopackUpdater(updateOptions.FeedUrl, sp.GetRequiredService<UpdateProxyDownloader>()),
            sp.GetRequiredService<UpdateProxyDownloader>()));
        collection.AddLogging(b => b.AddProvider(ApplicationLogging.Provider));
        collection.AddSingleton<IAtomicFileWriter, AtomicFileWriter>();
        collection.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(Path.Combine(AppPaths.UserDirectory, "settings.json"), sp.GetRequiredService<IAtomicFileWriter>()));
        collection.AddSingleton<IBaseConverter, BaseConverter>();
        collection.AddSingleton<IAsciiHexConverter, AsciiHexConverter>();
        collection.AddSingleton<ICp500Converter, Cp500Converter>();
        collection.AddSingleton<SessionSettings>();
        collection.AddSingleton<FileDialogService>();
        collection.AddSingleton(new ToolDescriptor("base-converter", "Base Converter", "Convert numeric bases, ASCII and EBCDIC CP500 text and hex bytes.", ToolCategory.Calculators, NavigationIcons.BaseConverter));
        collection.AddSingleton(new ToolDescriptor("text-cleanup", "Text Cleanup", "Remove spaces, tabs, line endings, digits, punctuation and selected characters from pasted text.", ToolCategory.Utilities, NavigationIcons.TextCleanup));
        collection.AddTransient<TextCleanupWorkspace>();
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<TextCleanupWorkspace>("text-cleanup", sp));
        collection.AddSingleton<IWorkspaceViewFactory, TextCleanupViewFactory>();
        collection.AddSingleton(new ToolDescriptor("text-compare", "Text Compare", "Compare two texts and highlight added and removed characters side by side.", ToolCategory.Utilities, NavigationIcons.TextCompare));
        collection.AddTransient<TextCompareWorkspace>();
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<TextCompareWorkspace>("text-compare", sp));
        collection.AddSingleton<IWorkspaceViewFactory, TextCompareViewFactory>();
        collection.AddSingleton(new ToolDescriptor("log-parser", "Log Parser", "Detect and inspect log formats supplied by extensions or custom definitions.", ToolCategory.Parsers, NavigationIcons.LogParser));
        collection.AddSingleton(new ToolDescriptor("message-compare", "Message Compare", "Compare two messages by spec, including fields and decoded subfields.", ToolCategory.Parsers, NavigationIcons.MessageCompare));
        collection.AddTransient<MessageCompareWorkspace>();
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<MessageCompareWorkspace>("message-compare", sp));
        collection.AddSingleton<IWorkspaceViewFactory, MessageCompareViewFactory>();
        collection.AddSingleton(new ToolDescriptor("file-generator", "File Generator", "Validate, preview and save fixed-width records.", ToolCategory.Generators, NavigationIcons.FileGenerator));
        collection.AddSingleton(new ToolDescriptor("emv-decoder", "EMV Tag Decoder", "13 EMV tags, scheme profiles, Form Factor / Third Party Data, CVM, TTQ, CTQ and BER-TLV.", ToolCategory.Payments, NavigationIcons.EmvDecoder));
        collection.AddSingleton(new ToolDescriptor("iso8583-bitmap", "ISO 8583 Bitmap", "Decode hex/binary bitmaps and encode primary/secondary field lists.", ToolCategory.Payments, NavigationIcons.IsoBitmap));
        collection.AddSingleton(new ToolDescriptor("luhn", "Luhn Check Digit", "Validate payment card checksums and calculate a Luhn check digit.", ToolCategory.Payments, NavigationIcons.Luhn));
        collection.AddSingleton(new ToolDescriptor("arqc", "ARQC Calculator", "Calculate and verify session-key application cryptograms with ISO 9797-1 algorithm 3.", ToolCategory.Payments, NavigationIcons.Arqc));
        collection.AddSingleton<IToolRegistry, ToolRegistry>();
        collection.AddSingleton(new ToolDescriptor("bitwise-calculator", "Bitwise Calculator", "AND, OR, XOR, NOT, logical shifts and rotations in binary, decimal and hex.", ToolCategory.Calculators, NavigationIcons.Bitwise));
        collection.AddSingleton(new ToolDescriptor("base64-calculator", "Base64", "Encode/decode UTF-8 text and hex bytes with standard or URL-safe Base64.", ToolCategory.Calculators, NavigationIcons.Base64));
        collection.AddSingleton(new ToolDescriptor("3des-calculator", "3DES Calculator", "Encrypt/decrypt hex bytes with two-key or three-key Triple DES, ECB/CBC and explicit padding.", ToolCategory.Calculators, NavigationIcons.TripleDes));
        collection.AddSingleton(new ToolDescriptor("certificate-inspector", "X.509 Certificate Inspector", "PKI certificate inspection: PEM/DER, validity, SANs, fingerprints, RSA modulus/exponent and public key export.", ToolCategory.Utilities, NavigationIcons.Certificate));
        collection.AddTransient<CertificateWorkspace>();
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<CertificateWorkspace>("certificate-inspector", sp));
        collection.AddSingleton<IWorkspaceViewFactory, CertificateViewFactory>();
        collection.AddSingleton(new ToolDescriptor("network-relay", "TCP / TLS Relay", "Network proxy / MITM: forward raw TCP, inspect client and server hex/text, terminate TLS and create test certificates.", ToolCategory.Utilities, NavigationIcons.NetworkRelay));
        collection.AddTransient<NetworkRelayWorkspace>();
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<NetworkRelayWorkspace>("network-relay", sp));
        collection.AddSingleton<IWorkspaceViewFactory, NetworkRelayViewFactory>();
        collection.AddSingleton(new ToolDescriptor("test-telnet-server", "Test Telnet Server", "Start an ASCII, ANSI/VT100 or simulated Telserv/TN6530 test server for terminal clients and relay testing.", ToolCategory.Utilities, NavigationIcons.TelnetServer));
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new TestTelnetServerWorkspaceFactory(sp.GetRequiredService<PluginCatalog>()));
        collection.AddSingleton<IWorkspaceViewFactory, TestTelnetServerViewFactory>();
        collection.AddSingleton(new ToolDescriptor("timed-key-presser", "Timed Key Presser", "Press a selected keyboard key at a timed interval, with Start/Stop and a timestamped activity log.", ToolCategory.Utilities, NavigationIcons.TimedKeyPresser));
        collection.AddSingleton<IKeyboardInput, WindowsKeyboardInput>();
        collection.AddSingleton<IToolWorkspaceFactory>(sp => new WorkspaceFactory<TimedKeyPressWorkspace>("timed-key-presser", sp));
        collection.AddSingleton<IWorkspaceViewFactory, TimedKeyPressViewFactory>();
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
            .Select(d => (ProtoBench.Parsing.Abstractions.ILogParser)new JsonDefinedParser(d)).ToList();
        allParsers.AddRange(messages.Definitions.Concat(extraMessages.Definitions.Where(d => Accept(d.Id, parserIds)))
            .Select(d => new RecordMessageParser(d, fieldDecoders)));
        allParsers.AddRange(plugins.Plugins.SelectMany(p => p.Parsers));
        collection.AddSingleton<IParserRegistry>(new ParserRegistry(allParsers));
        collection.AddSingleton<IGeneratorRegistry>(new GeneratorRegistry(generators.Definitions
            .Concat(extraGenerators.Definitions.Where(d => Accept(d.Id, generatorIds))).Select(d => (ProtoBench.Generation.Abstractions.IFileGenerator)new FixedWidthGenerator(d))
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
