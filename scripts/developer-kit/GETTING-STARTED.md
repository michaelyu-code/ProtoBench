# ProtoBench developer kit

Use the `ProtoBench.*` SDK assemblies, namespaces and props imports. The desktop executable is `app/ProtoBench.exe`. SDK API 2 requires rebuilding extensions against these assemblies and setting `apiVersion` to 2 in their manifests.

This kit builds independently of the original application repository. Extract the entire ZIP before building; keep `sdk`, `samples` and `tools` in their relative locations. Exact SDK/runtime versions are recorded in `versions.json`.

## Requirements

Use Windows x64, PowerShell and the .NET 10 SDK. The SDK includes the .NET runtime required by the test application. The custom WinUI example also restores the matching Microsoft.WindowsAppSDK NuGet package, so its first build needs NuGet access. No original app source or project references are required.

## First plugin

From this folder:

```powershell
./tools/Build-PluginPackage.ps1 -ProjectPath ./samples/ExamplePlugin/ExamplePlugin.csproj
```

This builds the sample and writes `packages/example.toolkit-1.0.0.nstplugin`. A prebuilt copy is included. It demonstrates a standard SHA-256 tool, a log parser, a file generator and a field decoder with a JSON message profile. Copy the sample folder to start your own plugin; change the assembly/namespace, entry type, unique IDs and manifest file names together.

For a specialized UI, build the programmatic WinUI counter:

```powershell
./tools/Build-PluginPackage.ps1 -ProjectPath ./samples/ExampleWinUIPlugin/ExampleWinUIPlugin.csproj
```

It produces `packages/example.winui-1.0.0.nstplugin`. Custom compiled XAML/PRI resource registration is not supplied by the host; use programmatic views as shown.

## Compile against the SDK

Standard tools, parsers, generators and field decoders import `sdk/ProtoBench.PluginSdk.props` in their project. It references all portable contract DLLs under `sdk/net10.0` with `Private=false`, so the host's SDK copies are not bundled into your plugin. Keep `<EnableDynamicLoading>true</EnableDynamicLoading>` in plugin projects.

Custom WinUI tools import `sdk/ProtoBench.PluginSdk.WinUI.props` instead and use the framework/WindowsAppSDK settings shown in the custom-UI starter. Do not include toolkit SDK or WinUI runtime DLLs in your plugin manifest. Declare any additional private dependencies and resources explicitly in `plugin.json`.

Browse `contracts/` for interface/result-model source and [the full plugin guide](docs/PLUGINS.md) for lifecycle, field descriptions, decoder behavior, source offsets and package limits. Paths beginning with `src/` in that guide refer to the original repository; the standalone starters in this kit use binary references instead.

Field details have **Decoded**, **Checks**, and **Specification** tabs. Set `FieldDecodeResult.Specification` to preformatted plain text to supply specification paragraphs and aligned tables. The host preserves spaces and line breaks in a monospaced block with no wrapping. See the response decoder in `samples/ExamplePlugin/ExamplePlugin.cs` for an example. Use the matching updated SDK and app when using this property.

For large log files and clipboard content, implement `IStreamingLogParser.ParseToAsync` and emit records sequentially to `ILogParseSink`. The example parser forwards to the JSON engine's streaming method. The host indexes results on disk with 2,000 messages per page by default; full-log search and filters are independent of page size. Legacy `ILogParser` plugins remain available for inputs up to 16 MiB. Use **Settings → Reload app** to apply plugin changes.

## Test in the app

If `app/` is included, launch `app/ProtoBench.exe`. Otherwise use a matching app distribution supplied by the maintainer. The test app uses the normal `%LOCALAPPDATA%/ProtoBench` settings, definitions and plugins; it does not create an isolated profile.

Open **Settings → Plugins & custom tools → Import plugin…**, choose your `.nstplugin` package, and restart. Tools appear under **Custom Tools**; parsers and generators appear in their existing selectors. Open two tabs to check session independence. The custom UI counter starts at zero in each tab.

Plugins execute developer code with the app's permissions. Imports and enable/disable changes take effect after restart. For a replacement, close the app, remove only your plugin's folder using **Open plugins folder**, and import the new package after reopening.

## Kit contents

- `sdk/`: portable and optional WinUI SDK binaries, debugging symbols and MSBuild reference helpers.
- `contracts/`: browsable public API source; not part of starter compilation.
- `samples/`: standalone standard and custom-UI plugin projects.
- `tools/Build-PluginPackage.ps1`: builds either starter or your own compatible project; packages only declared files.
- `packages/`: prebuilt sample plugins.
- `docs/PLUGINS.md`: developer guide.
- `app/`: optional Windows x64 application with all required deployment files; requires the .NET 10 runtime supplied by the developer SDK.
- `versions.json` and `SHA256SUMS.txt`: version information and file hashes. Hashes detect changes; they do not authenticate the sender.


