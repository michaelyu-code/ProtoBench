# Developer plugins

ProtoBench loads trusted .NET plugins for custom tools, complete log parsers, file generators and reusable message-field decoders. Plugins are loaded at startup. Use **Settings → Reload app** after importing or enabling/disabling plugins or changing custom definitions. This restarts the app; open workspaces and inputs are closed, with a confirmation when workspaces are open. Parser and message specs come from extensions or custom user definitions; the app bundles no parser specs. Custom tools appear in the **Custom Tools** sidebar group, dashboard search, favorites and new-tab picker. Each tool tab receives a separate session.

## Import and manage

Open **Settings → Plugins & custom tools → Import plugin…**. Select a `.nstplugin` (ZIP) package, `.zip`, `plugin.json`, or an entry DLL beside `plugin.json`. Review the package name/version, then import and restart. Import copies the declared files; it does not execute code. The entry type executes when the app next starts.

Plugins live in `%LOCALAPPDATA%/ProtoBench/plugins/<id>/`. The Settings checkbox enables/disables the next startup; currently loaded tools remain available until restart. Settings shows whether a package is loaded this session. Startup notifications identify rejected manifests and load/registration failures. To replace or remove a plugin, close the app, remove its specific folder using **Open plugins folder**, then import the replacement after reopening. Existing IDs cannot be overwritten during import. There is no hot reload.

Plugins run with the host process's permissions. Assembly loading contexts separate dependencies; they are not a security sandbox or a crash/timeout boundary. Install developer code you trust. Ordinary managed detection/decoding errors are contained, but native crashes, process termination and non-cooperative infinite loops can still affect the application. A plugin that requires isolation must run its own constrained worker process; that is outside this API.

## Build the example

From the repository root:

```powershell
& ./samples/plugins/Build-ExamplePlugin.ps1
```

Import the resulting `artifacts/example-toolkit.nstplugin`, then restart. It adds:

- **SHA-256 text** under Custom Tools. `abc` produces `BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD`.
- **Example EX: log**, available in Log Parser. Paste `EX:hello` and parse automatically.
- **Example eight-byte record**, available in File Generator. Input `{"value":"demo"}` produces ASCII `demo` plus four spaces and CRLF.
- Decoder `example.response` and the bundled **Synthetic Term response example** profile. Select this profile manually and paste:

```text
DEMO RECORD 1 FROM 17Jan 14:14:58.83 OUT ON DEMO SEQ: 00001
 Msg-type=0210 Resp code=00
```

The response field displays its spec description and a decoded “Approved (synthetic)” child. This is synthetic demonstration data, not a real Term rule set. The example is built by tests but is not automatically installed into the user's app.

## SDK and package contract

To create a complete kit for another developer, run `./scripts/Build-DeveloperKit.ps1`. It builds the SDKs and matching test app, includes their dependencies and contract source, generates standalone standard/WinUI starter projects, verifies both starters, and creates `artifacts/developer-kit/` plus `artifacts/developer-kit.zip`. The kit's README explains binary references and packaging without the app repository. Use `-SkipApp` for an SDK-only handoff, `-NoZip` for folder output only, or `-OutputDirectory <new-folder>` to choose another destination. Existing output is preserved; use a new destination for a later build. The default test app is framework-dependent and runs with the .NET 10 runtime included in the developer SDK.

Use the .NET 10 SDK. Reference `src/ProtoBench.PluginSdk/ProtoBench.PluginSdk.csproj`, or distribute the SDK and its contract assemblies to developers. Keep contracts at the app's supported API/assembly versions. A plugin project should set `<EnableDynamicLoading>true</EnableDynamicLoading>` so dependency metadata and private runtime dependencies are available in build output.

Implement one public, concrete, parameterless `IToolkitPlugin` entry type. Its optional `Tools`, `Parsers`, `Generators`, and `FieldDecoders` properties export capabilities; omitted properties export nothing. Use package-qualified IDs such as `acme.response-code`. Tool/parser/generator IDs are case-insensitively unique within their respective registries. Decoder IDs are case-sensitive, and `emvEntries` is reserved. Processing-code decoding is provided by extensions. The whole package is rejected if any export conflicts. `Dispose` releases package-wide resources.

A manifest at the package root declares exactly the files to import:

```json
{
  "schemaVersion": 1,
  "apiVersion": 2,
  "id": "example.toolkit",
  "name": "Example Toolkit Extensions",
  "version": "1.0.0",
  "entryAssembly": "ExamplePlugin.dll",
  "entryType": "ExamplePlugin.ExamplePlugin",
  "files": ["ExamplePlugin.dll", "ExamplePlugin.deps.json"]
}
```

`schemaVersion` must be 1 and `apiVersion` must be 2. `version` is a numeric .NET version (for example `1.2.0`). IDs use lowercase ASCII letters, digits, periods or hyphens, start with a letter/digit, and have at most 80 characters. File paths are relative, case-sensitive portable names using `/`; no rooted paths, traversal, Windows device names, hidden/reserved files or symbolic links/junctions. Maximum manifest size is 64 KiB, at most 512 declared files, and compressed/expanded packages are limited to 128 MiB. ZIPs must contain only the manifest and declared files (directory entries are allowed). Missing, duplicate or undeclared entries reject the import and remove its staging folder.

Include private dependency DLLs, `.deps.json` and resources in `files`. Each package uses its own `AssemblyLoadContext` with `AssemblyDependencyResolver`. The SDK, Core, Parsing, Generation and WinUI contract assemblies use the host's instances to preserve type identity; do not ship replacements for them. Target the host framework, Windows/x64 runtime for native dependencies, and API version. The manifest is a compatibility declaration, not a digital signature or verification of a developer's identity.

Optional schema-version 1 JSON definitions belong under `definitions/messages/`, `definitions/parsers/`, or `definitions/generators/` and must also be listed in `files`. They load only after the containing plugin successfully loads. Existing bundled/user formats and exported C# formats take precedence over colliding packaged JSON IDs; collisions produce diagnostics. Avoid collisions rather than relying on precedence between plugins.

## Standard tools

Export a `PluginTool` with an ID, name, description, ordered `ToolInput` list and a `CreateSession` factory. Inputs support Text, Multiline, Secret (masked) and Choice. Give Choice inputs a nonempty choices list containing their default value. A standard form supports at most 64 inputs, required checks, Run, Clear inputs, Cancel, text output, Copy result and optional hierarchical result fields. The host does not persist tool inputs or results.

Each factory call must return a **new** `IPluginToolSession`. Its `ExecuteAsync` receives a read-only snapshot of input strings and a cancellation token, and returns `PluginToolResult(Text, Fields?)`. Execution is off the UI thread; never access WinUI controls from this method. Honor cancellation and enforce resource limits appropriate to the operation. The host disables Run during execution. Closing a tab cancels its operation and disposes the session after that operation ends; a non-cooperative session can delay its own disposal. Treat masked input as ordinary managed strings, not guaranteed-erased secret storage.

## Custom WinUI views

For specialized interfaces, additionally reference `src/ProtoBench.PluginSdk.WinUI/ProtoBench.PluginSdk.WinUI.csproj`. Target `net10.0-windows10.0.26100.0`, x64 and the host's Microsoft.WindowsAppSDK version (currently 2.6.5). Implement `ICustomToolViewProvider` on the package entry type:

```csharp
public FrameworkElement? CreateView(string toolId, IPluginToolSession session)
{
    if (toolId != "acme.special-tool") return null;
    return new MyProgrammaticWinUIView((MySession)session);
}
```

The host calls `CreateView` on the UI thread with the tab's newly created session; return null to use the standard form. The host still owns session disposal. The custom view owns its UI, input validation, execution/cancellation, error presentation and cleanup subscriptions. Make session disposal cancel any work the custom UI starts. Programmatic WinUI views are supported; automatic registration of a plugin's compiled XAML/PRI resources is not supplied. Use the exact compatible WinUI SDK and avoid packaging duplicate runtime/contract assemblies.

## Full parsers and generators

Set each `ParsedRecord.RawText` to the original record text, including its internal blank lines, indentation and line terminators. The host retains this string unchanged in the disk index. The Original tab displays it with single-spaced paragraphs; **Copy original** and copying a selection preserve the line endings supplied by the extension (including mixed CRLF/LF/CR). Literal `\n` text is not unescaped. If an extension trims, joins or normalizes lines before returning `RawText`, the host cannot recover the discarded formatting. Keep `SourceRange.CharacterOffset` and `CharacterLength` relative to that unchanged string; the host maps them to the displayed paragraphs for source highlighting.

Export `ILogParser` or `IFileGenerator` instances. These appear in the existing Log Parser/File Generator selectors. Instances may serve multiple workspaces concurrently: keep per-operation state local or synchronize it. Dispose resources owned by the plugin in its `Dispose` method.

Parsers provide `FormatId`, `Name`, `DetectionPriority`, `CanParse(ReadOnlySpan<byte>)` and `ParseAsync(Stream, CancellationToken)`. Detection is a short synchronous prefix/sample probe; avoid I/O or expensive work. If a plugin probe throws, it is treated as no match so other parsers can still detect the input; manual selection remains available and parse errors surface through the workspace. Return `ParseResult` with records, nested `ParsedField` values, source ranges, optional message metadata and findings. `ParsedField.Description` is shown in the selected field's detail panel, and in nested-field tooltips. Absolute byte offsets refer to the original stream; character offsets/lengths are record-relative UTF-16. Do not claim source ranges for synthetic/derived fragments outside their actual containing source. Report truncation and enforce bounded reads/record counts for the legacy preview method.

For large files and clipboard input, implement **`IStreamingLogParser`** in addition to the existing preview method:

```csharp
public Task ParseToAsync(Stream input, ILogParseSink sink, CancellationToken token = default)
    => engine.ParseToAsync(input, sink, token); // JsonDefinedParser or RecordMessageParser
```

Custom streaming implementations must read in chunks, keep only the current record in memory, and call `sink.AddRecord(record)` sequentially. The host persists each record synchronously, then releases it. Call `sink.AddDiagnostic(...)` for rejected records and `sink.ReportProgress(bytesRead)` periodically. Use `long` for absolute offsets and counts, honor cancellation between records/reads, and retain a finite per-record bound so a malformed record cannot consume the entire file. Do not build a whole-file string/list before calling the sink. See the example plugin for a forwarding implementation. The interface is additive and uses SDK API version 2; plugins using it need the matching updated host/contracts.

The host indexes all streaming records on disk, pages summaries (2,000 by default), and reads full decoded payloads only for selection. Search, filtering, sorting and related-message candidate lookup use the full index. The UI displays at most 250 related candidates and explicitly reports when more exist. Oversized/malformed input can be skipped with diagnostics; only the first 1,000 diagnostics are displayed. Legacy plugins that only implement `ILogParser` are restricted to 16 MiB input; their own `IsTruncated` previews remain explicitly labelled. Built-in `ParseAsync` retains its original preview limits for callers, while `ParseToAsync` removes total byte/record preview caps. File and clipboard import support multi-gigabyte input subject to available disk space and the source application's clipboard capability.

**Related tab for streaming wrappers:** `RecordMessageParser` attaches `ParsedRecord.Correlation` (the profile's time window, compatible MTIs in both directions, matching field keys and connection scope). The host serializes these rules with the record, so lookup works even when only your custom `IStreamingLogParser` is registered and its `FormatId` differs from `Message.ProfileId`. Forward engine records unchanged. When adding lifecycle findings, use a record copy so all metadata and rules survive:

```csharp
sink.AddRecord(record with { Findings = combinedFindings });
```

If you construct a new `ParsedRecord` from engine output, explicitly copy `Message`, `Findings` and `Correlation`. A fully custom parser can supply `Correlation = new MessageCorrelation(windowSeconds, compatibleMessageTypes)` itself; the list contains the other side's MTIs (for a synthetic `0200` request, `["0210"]`; for its `0210` reply, `["0200"]`). Use your specification's actual pairs and a 1–86,400-second window. Missing prerequisites are explained in the Related tab. Existing plugins forwarding the shared host's `RecordMessageParser` output pick up the rules automatically; plugins constructing their own records need the updated SDK properties.

**Configurable correlation:** set these top-level properties in a message profile to match on STAN + transmission date/time and permit replies on a different connection:

```json
"CorrelationKeys": ["11", "7"],
"CorrelationSameConnection": false,
"CorrelationWindowSeconds": 300
```

Omitting the new properties preserves `CorrelationKeys: ["11", "41"]` and `CorrelationSameConnection: true`. Keys are field IDs (canonical names or logged labels also work, case-insensitively). Every configured key must have a nonempty, exactly equal value on both messages; empty/missing keys never match. There is no implicit DE11 or DE41 requirement when they are not listed. Profiles accept 1–32 unique nonblank keys, up to 128 characters each. Declare fields such as DE7 in `Fields` with their logged aliases so `RecordMessageParser` assigns the intended field ID. A literal `Fld7` label without a field definition is retained as an unknown label, not inferred as field ID `7`.

Custom parsers can choose the same rules per record without a JSON profile:

```csharp
Correlation = new MessageCorrelation(300, ["0210"])
{
    Keys = ["11", "7"],
    SameConnection = false
};
```

Both `MessageAnalysis.Related(record, all, record.Correlation)` and the disk-indexed Related tab apply these rules. Matching still requires the same profile, compatible explicit MTIs, opposite directions, and parseable `Message.LoggedDateTime` (`dMMM HH:mm:ss.FFFFFFF`) values within the configured window. DE7 is an equality key, not a replacement for that log-time window. The host does not infer missing timestamps or guess request/reply pairs. Cross-connection matching does not drop any of these other constraints. All ambiguous candidates are retained (the UI displays up to 250). The index narrows by profile/time and common keys before evaluating custom fields, without loading the full log's decoded payloads. Use the updated host and SDK together; reparse open logs after changing a profile's correlation policy.

Generators implement the existing contract in `Generation/Abstractions/IFileGenerator.cs` and return output diagnostics; use cancellation and stage output before writing when validation can fail.

## Field decoders and spec descriptions

Export `IFieldDecoder` with a unique namespaced ID. A Term profile can reference it while retaining the existing RECORD log engine:

```json
{
  "Id": "39",
  "Name": "Response code",
  "Labels": ["Resp code"],
  "Type": "digits",
  "Length": 2,
  "Description": "Explanation transcribed from the supplied specification.",
  "Decoder": "example.response"
}
```

The field description is available without a decoder. Existing version-1 definitions continue to work. `Decode` receives the profile ID, MTI, parsed field, all first-occurrence field values, and cancellation token. Return decoded child fields and optional `FieldFinding`s. Keep decoders thread-safe and avoid mutating context objects. Missing decoders produce an Undetermined finding; throwing decoders produce a Review finding. Both retain the raw field value. Built-in processing-code/ICC decoders remain supported.

The field detail panel keeps **Complete value**, Copy and View source above three tabs: **Decoded** (description and child fields), **Checks** (findings for the selected field), and **Specification**. Supply optional formatted plain text through `FieldDecodeResult.Specification`:

```csharp
return new FieldDecodeResult(children, findings)
{
    Specification = "C 4.19 Field 39 Response Code\n\n"
        + "Field 39 value   Tag 8A value  Outcome\n"
        + "───────────────  ────────────  ────────\n"
        + "00 or 08         3030          Approved\n"
        + "any other value  3035          Declined"
};
```

The host transfers this text to `ParsedField.Specification` and displays it in a selectable, read-only monospaced block with horizontal and vertical scrolling and no wrapping. Spaces and line breaks are preserved; Markdown and HTML are not interpreted. Use spaces for column padding and insert paragraph line breaks yourself. The plugin owns the content, headings and table layout; do not include the surrounding panel or tab labels. Complete parsers can set `ParsedField.Specification` directly. Empty or omitted text shows “No specification supplied for this field.” Existing plugins can omit this additive property; the API version and existing constructors are unchanged. Rebuild plugins using it against the updated SDK and run them with the updated host.

The `spec-to-tool-config` skill can transcribe field descriptions and target known installed decoder IDs. New decoder algorithms and custom tools require developer code; the skill must not invent decoder IDs merely to make a spec appear covered.

## Verification

`dotnet build ProtoBench.slnx` checks both portable and WinUI SDKs. `dotnet test ProtoBench.slnx` covers import rollback/path validation, incompatible API versions, disabled plugins, duplicate IDs, isolated DLL loading with shared contracts, tool execution, CRLF parser offsets, decoder/description flow, unavailable decoders, and independent session cancellation/disposal. For UI verification, import a sample, restart, open its tool through sidebar/dashboard/new-tab, run it in two tabs, and check its parser/description/decoder in Log Parser.


