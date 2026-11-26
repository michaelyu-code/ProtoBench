# Developer plugins

ProtoBench loads trusted .NET plugins for custom tools, complete log parsers, file generators and reusable message-field decoders. Plugins are loaded at startup. Use **Settings → Reload app** after importing or enabling/disabling plugins or changing custom definitions. This restarts the app; open workspaces and inputs are closed, with a confirmation when workspaces are open. Parser and message specs come from extensions or custom user definitions; the app bundles no parser specs. Custom tools appear in the **Custom Tools** sidebar group by default, or in an existing menu chosen by the developer, as well as dashboard search, favorites and the new-tab picker. Each tool tab receives a separate session.

## Import and manage

Open **Settings → Plugin manager → Import plugin…**. Select a `.nstplugin` (ZIP) package, `.zip`, `plugin.json`, or an entry DLL beside `plugin.json`. Review the package name/version, then import and restart. Import copies the declared files; it does not execute code. The entry type executes when the app next starts.

Plugins live in `%LOCALAPPDATA%/ProtoBench/plugins/<id>/`. The Settings checkbox enables/disables the next startup; currently loaded tools remain available until restart. Settings shows whether a package is loaded this session. Startup notifications identify rejected manifests and load/registration failures. To update a plugin, import a package with the same ID and a newer numeric version, then use **Reload app**. Import validates and stages the complete replacement without changing the running plugin. Settings shows the pending version; the next startup applies it before loading plugins, preserves the current enabled setting, and removes files omitted by the replacement. A still newer import can replace a pending update. Equal versions and downgrades are rejected. Invalid imports leave the installed plugin and any pending update intact. To remove a plugin, close the app and remove its folder and any `.update-<id>` folder using **Open plugins folder**. There is no hot reload.

Plugins run with the host process's permissions. Assembly loading contexts separate dependencies; they are not a security sandbox or a crash/timeout boundary. Install developer code you trust. Ordinary managed detection/decoding errors are contained, but native crashes, process termination and non-cooperative infinite loops can still affect the application. A plugin that requires isolation must run its own constrained worker process; that is outside this API.

## Optional online updates

Plugins can opt into online updates by adding an `updateUrl` property to `plugin.json`:

```json
"updateUrl": "https://plugins.example.com/example.toolkit/latest.json"
```

The URL must use HTTPS without embedded credentials or a fragment. Omit the property for manual-only updates; existing packages continue to work. The host reads this metadata without executing the plugin. Update checks run only when requested in **Settings → Plugin manager**, individually or using **Check all for updates**. Disabled plugins can also be checked. Each plugin shows its update source, result, release notes and a download button when a newer compatible release exists. Plugins without an update URL show a manual-import explanation.

Publish a JSON feed at that URL:

```json
{
  "id": "example.toolkit",
  "version": "1.1.0",
  "apiVersion": 2,
  "packageUrl": "https://plugins.example.com/example.toolkit/1.1.0.nstplugin",
  "sha256": "<64 hexadecimal characters from the package's SHA-256 hash>",
  "releaseNotes": "Changes in this version."
}
```

`id` must match the installed plugin, `version` must be a newer numeric version than both the installed and pending versions, and `apiVersion` must match the host SDK API. `packageUrl` must be an absolute HTTPS URL without credentials or a fragment and return a `.nstplugin`/ZIP package. Generate its checksum with `(Get-FileHash ./example.toolkit.nstplugin -Algorithm SHA256).Hash`. Release notes are optional plain text. The feed is limited to 64 KiB, downloads and expanded packages to 128 MiB, and each check/download to five minutes.

**Download** verifies the package checksum and its manifest's ID, version and API, then uses the same validated staged import as a manual update. A failure leaves the installed plugin and any pending update intact. **Reload app** applies downloaded updates; enabled/disabled choices are preserved. Checksums detect damaged or mismatched downloads; trust still depends on the developer and their HTTPS feed. This is a new host feature: plugins using `updateUrl` require a host with this support because earlier hosts reject unknown manifest properties.

### Private GitLab projects

GitLab.com and self-managed GitLab projects can use authenticated API URLs. For example, if project `12345` contains `updates/latest.json`, set:

```json
"updateUrl": "https://gitlab.example.com/api/v4/projects/12345/repository/files/updates%2Flatest.json/raw?ref=main"
```

The feed uses the same format as above. Its `packageUrl` can point to a packaged plugin committed under a version tag:

```json
"packageUrl": "https://gitlab.example.com/api/v4/projects/12345/repository/files/dist%2Fexample.toolkit-1.1.0.nstplugin/raw?ref=v1.1.0"
```

Replace the server, numeric project ID, URL-encoded file paths and refs with your project's values. Use GitLab's API raw-file URLs rather than web UI blob/raw pages that can redirect to sign-in. The token's account must have access to the project. For both files served through the [Repository Files API](https://docs.gitlab.com/api/repository_files/), a personal access token with `read_repository` is sufficient; `read_api` also supports reading these files. If binaries use a different API such as the package registry, choose a token with read permission for that API as well. This implementation uses GitLab's `PRIVATE-TOKEN` header; deploy tokens and CI job tokens using other header types are not supported.

In **Settings → Plugin manager**, expand the plugin's **Private GitLab access**, paste the token, and select **Save token**. Check for updates, then download the offered version. Use Save token again to rotate it, or **Remove token** to forget it. Do not put a token in the manifest, feed, package URL or repository.

Tokens are stored in the [Windows Credential Locker](https://learn.microsoft.com/en-us/windows/apps/develop/security/credential-locker), outside plugin folders and JSON settings. Windows controls the vault's protection and any credential roaming. Each credential is scoped to one plugin ID and the feed's HTTPS origin (host and port). The updater sends it in a request header only to that origin, for both feed checks and package downloads. Redirects are followed explicitly, with at most five hops; HTTP destinations are rejected and other HTTPS origins receive no token. An external package host must therefore allow an anonymous or signed-URL download. Authentication/access failures show a token/permission hint. **Remove all local data** also removes ProtoBench's saved plugin update tokens.

## Build the example

From the repository root:

```powershell
& ./samples/plugins/Build-ExamplePlugin.ps1
```

Import the resulting `artifacts/example-toolkit.nstplugin`, then restart. It adds:

- **SHA-256 text** under Calculators, marked as a custom tool. `abc` produces `BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD`.
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

Implement one public, concrete, parameterless `IToolkitPlugin` entry type. Its optional `Tools`, `Parsers`, `Generators`, `FieldDecoders`, and `TelnetServers` properties export capabilities; omitted properties export nothing. Use package-qualified IDs such as `acme.response-code`. Tool/parser/generator IDs are case-insensitively unique within their respective registries. Decoder IDs are case-sensitive, and `emvEntries` is reserved. Processing-code decoding is provided by extensions. The whole package is rejected if any export conflicts. `Dispose` releases package-wide resources.

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

Export a `PluginTool` with an ID, name, description, ordered `ToolInput` list and a `CreateSession` factory. Inputs support Text, Multiline, Secret (masked), Choice, Integer and FieldValues. Give Choice inputs a nonempty choices list containing their default value. A standard form supports at most 64 inputs, required checks, Run, Clear inputs, Cancel, text output, Copy result and optional hierarchical result fields. The host does not persist tool inputs or results. Input edits invalidate the displayed result; Run, Clear and inputs are disabled during execution, while Cancel remains available.

Each factory call must return a **new** `IPluginToolSession`. Its `ExecuteAsync` receives a read-only snapshot of input strings and a cancellation token, and returns `PluginToolResult(Text, Fields?)`. Execution is off the UI thread; never access WinUI controls from this method. Honor cancellation and enforce resource limits appropriate to the operation. The host disables Run during execution. Closing a tab cancels its operation and disposes the session after that operation ends; a non-cooperative session can delay its own disposal. Treat masked input as ordinary managed strings, not guaranteed-erased secret storage.

### Structured spec search and field decoding (2.0.10+)

Portable plugins can opt into the shared result views without implementing WinUI. Build against the 2.0.10+ SDK and use a matching updated host. These additions keep API version 2 and assembly identity `2.0.0.0`; existing result constructors, deconstruction, and text/tree rendering continue to work. Rebuild plugins that use the new members. An older host cannot render these new contracts.

Set **one** of `PluginToolResult.Search` or `.Decoding` using an object initializer. `Text` is still raw/copyable output; for structured results it appears in a collapsed **Raw output** expander. `Fields` is used by the legacy text/tree view only. An empty `Text` is allowed for structured results, including a search with no hits. A custom `ICustomToolViewProvider` still takes precedence over the standard host views.

For search:

```csharp
return new PluginToolResult(rawOutput)
{
    Search = new ToolSearchResult(query, rankedHits)
    {
        HighlightTerms = searchTerms,
        TotalMatches = totalMatches // omit if the engine does not know the full count
    }
};

// Construct each hit from the real engine's result and source metadata:
var hit = new ToolSearchHit(sectionTitle, shortExcerpt, fullPassage)
{
    Reference = new ToolSpecReference(specNameAndVersion, sectionOrPage),
    Score = bm25Score // optional; displayed under Ranking details, never as confidence
};
```

Supply hits in relevance order: the host does not run BM25, rerank, paginate, or infer source metadata. It shows a selectable results list with excerpts and a passage preview, selects the first hit, emphasizes literal `HighlightTerms`, and provides **Copy passage**. List and preview reflow vertically in a narrow result pane. Zero hits show `EmptyMessage`. Scores and raw output stay collapsed. All titles, passages and references are plain text, not XAML or HTML.

For field decoding:

```csharp
return new PluginToolResult(rawOutput)
{
    Decoding = new ToolDecodeResult("DE39 · Response code", value, summary,
        [new ToolDecodeComponent("Transaction outcome", value, meaning)
         { Position = "Entire field" }])
    {
        Status = ToolDecodeStatus.Decoded,
        ContextUsed = [new("Specification", specVersion), new("Message type", mti)],
        References = [new(specVersion, sectionOrPage, supportingPassage)]
    }
};
```

The host shows field name, raw value, summary, labeled component/value/meaning rows, context used, specification expanders, and **Copy decoded result**. Components can supply `Children` for expandable bit/byte/subfield breakdowns and their own `Reference`. Populate `Warnings` and choose `PartiallyDecoded`, `Unmapped`, `InvalidInput`, or `MissingContext` as appropriate; do not substitute a guessed meaning for unknown raw values. `ContextUsed` must list only values actually used by your rules. The plugin remains responsible for decoding, validation, and provenance.

Use the optional input additions to improve the form:

```csharp
new ToolInput("max", "Max results", ToolInputKind.Integer, "10", Required: true)
    { Minimum = 1, Maximum = 100 };
new ToolInput("other", "Other fields", ToolInputKind.FieldValues, "{}")
    { IsAdvanced = true };
```

`Integer` renders a numeric editor and validates whole numbers and optional inclusive bounds before calling the session. Inputs marked `IsAdvanced` appear in a collapsed **Optional inputs** group. `FieldValues` renders editable field/value rows with add/remove actions. The session receives a JSON object of strings; call `ToolInputValues.ParseFieldValues(inputs["other"])` to read it. Empty input means no fields. Blank and duplicate field names (ignoring case and surrounding whitespace), non-string JSON values, and malformed objects are rejected; value whitespace and leading zeroes are preserved. A required FieldValues input must contain at least one field. Clear inputs restores the declared defaults.

`samples/plugins/StructuredResultsPlugin` demonstrates both views with synthetic content. The developer kit includes its standalone project, importable package, and the new contract source. The sample search filters a fixed dataset rather than implementing BM25; its decoder only defines a small synthetic DE39 mapping. Adapt the existing real search/decoder engine to return these contracts.

## Existing menus and custom-tool badges

Set the optional `PluginTool.Category` initializer to `PluginToolCategory.Parsers`, `Converters`, `Generators`, `Comparisons`, `Utilities`, `Payments`, or `Calculators`. The default is `CustomTools`, so existing plugins keep their placement. Unknown or retired category values also fall back to **Custom Tools**, keeping the tool available when menus change. SDK category numbers are stable identifiers and must never be renumbered or reused for different menus. The host retains the tool's own icon and adds a puzzle badge with a **Custom tool** tooltip in the sidebar, plus a puzzle icon and **Custom tool** label on the dashboard (including favorites) and in the new-tab picker. IDs remain globally unique; choosing a menu does not replace built-in tools. These additive contracts retain API version 2 and assembly identity 2.0.0.0; rebuild plugins using them against the updated SDK and run with the matching host.

```csharp
new PluginTool("acme.calculate", "My calculator", "Developer calculation.",
    [], () => new CalculatorSession())
{
    Category = PluginToolCategory.Calculators
};
```

## Custom HTML screens (WebView2)

Set `PluginTool.WebView = new PluginWebView(html)` to replace the standard form with a complete HTML/CSS/JavaScript screen. This needs only the portable SDK; developers can use their preferred web UI framework and bundle its output into one HTML string. Embed resources inline (including images as data URLs); HTML must be nonempty and at most 2,000,000 UTF-8 bytes. The host uses the Microsoft Edge WebView2 Runtime and displays an error if initialization fails. Native `ICustomToolViewProvider` views take precedence when they return a view.

The HTML owns layout, scrolling, accessibility, input validation feedback and responsive behavior. Include a viewport meta tag and support keyboard navigation, text scaling and light/dark themes. A separate webview and `IPluginToolSession` are created per tab; switching tabs retains the screen, and closing a tab closes its browser and cancels pending execution before disposing the session. Browser storage is shared per tool ID under the app's local-data folder; avoid storing secrets. Plugins are trusted process code, as described above. The host exposes no native host objects, blocks top-level navigation away from `about:blank`, child-frame navigation, popups and downloads, and denies browser permission requests. This surface hosts packaged screens; external websites are not supported as tool screens.

Call `window.chrome.webview.postMessage` with a JavaScript **object** (do not stringify it). Each request needs a string `id` of 1–128 characters. Requests are limited to 1 MiB of text. Supported messages:

| Request | Behavior | Reply |
| --- | --- | --- |
| `{type:'execute', id:'1', inputs:{text:'hello'}}` | Updates declared input IDs, validates inputs and runs the tab's session off the UI thread. Omitted inputs retain their current/default values. | `{type:'result', id, success, result, status}` |
| `{type:'cancel', id:'2'}` | Requests cancellation of the current execution. | `{type:'ack', id, success:true}` |
| `{type:'clear', id:'3'}` | Resets inputs/results to defaults while idle. | `{type:'ack', id, success:true}` |

Malformed/unknown requests, unknown input IDs, non-string values and busy execute/clear requests receive `{type:'error', id, success:false, error}`. Invalid requests do not partially change inputs. Session failures, validation failures and cancellation return a result reply with `success:false`, `result:null` and an explanatory `status`. Results use camelCase JSON, including `result.text`, `result.fields`, `result.search` and `result.decoding`. Render returned content as text unless you intentionally sanitize HTML. Match responses by ID; a cancellation acknowledgement may arrive before the execution result. Disable Run while awaiting its reply, and provide Cancel for long operations.

```javascript
window.chrome.webview.addEventListener('message', ({data}) => {
    if (data.type === 'result') {
        document.querySelector('#output').textContent =
            data.success ? data.result.text : data.status;
    }
});
window.chrome.webview.postMessage({type:'execute', id:'1', inputs:{text:'hello'}});
```

`samples/plugins/WinUIExamplePlugin/Plugin.cs` includes a complete HTML counter alongside its native counter. Both call independent per-tab C# sessions; the HTML counter demonstrates the portable `WebView` property without implementing `ICustomToolViewProvider` for that tool. You can also embed an HTML file as an assembly resource and read it when creating the tool definition; it does not require loose package assets.

## Calculator plugins and shared utilities

Calculator plugins can use the same standard responsive Inputs/Results layout as other tools. Export a `PluginTool` with `ActionLabel: "Calculate"`, ordered text/secret/choice inputs, and a fresh `IPluginToolSession` per tab. Return the result and any explanation in `PluginToolResult.Text`. The optional WinUI SDK supports custom controls, including checkboxes; the standard form supports boolean options using a Choice input with Yes/No values.

The developer kit includes `ProtoBench.Utilities.dll` through `sdk/ProtoBench.PluginSdk.props`. Repository plugins also receive it transitively through the portable SDK. Developers can reuse `ProtoBench.Utilities.Binary.TripleDesCalculator.Transform`, `KeyCheckValue`, and `TransformDetailed` instead of reimplementing 3DES. The detailed API returns separate `Output` and `Details` strings, shows padding and block-level chaining, and includes key components only when `includeKeyValues: true` is explicitly passed. It does not trace DES's internal rounds. Built-in desktop view factories are internal app UI, not public SDK templates.

For example, a standard-form KCV calculator can export:

```csharp
new PluginTool("acme.kcv", "3DES KCV", "Calculate an encrypt-zero key check value.",
    [new ToolInput("key", "3DES key (hex)", ToolInputKind.Secret, Required: true)],
    () => new KcvSession(), ActionLabel: "Calculate");

public sealed class KcvSession : IPluginToolSession
{
    public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var kcv = ProtoBench.Utilities.Binary.TripleDesCalculator.KeyCheckValue(inputs["key"]);
        return Task.FromResult(new PluginToolResult(kcv));
    }
    public void Dispose() { }
}
```

Rebuild against the matching updated kit and host to use newly added utility APIs.

## Telnet server profiles

Export `IToolkitPlugin.TelnetServers` to add options to **Utilities → Test Telnet Server → Server profile**:

```csharp
using ProtoBench.Core.Networking;

public IEnumerable<TelnetServerProvider> TelnetServers =>
[
    new("acme.terminal", "Acme terminal", "Acme terminal simulator.",
        () => new MyTerminalSession())
];
```

Implement `ITelnetServerSession.RunAsync(ITelnetServerConnection, CancellationToken)` and `Dispose()`. Return a fresh session for every client; the factory may run concurrently. The host runs sessions off the UI thread and owns listening, optional immediate TLS, the eight-client limit, five-minute read idle timeout, traffic capture and socket cleanup. Sessions receive application bytes after TLS decryption and own Telnet negotiation, framing, terminal behavior and input limits. No built-in greeting or negotiation is added for plugin profiles.

Use connection `ReadAsync`, `WriteAsync` and `Log`; honor cancellation, stop on a zero-byte read, and release session resources in `Dispose`. The connection is valid only during `RunAsync`. Ordinary factory/session failures are logged and close that client while the listener remains available. Non-cooperative plugin code can delay shutdown, like other in-process extensions.

Provider IDs are case-insensitively unique. `builtin.ascii`, `builtin.ansi-vt100`, and `builtin.telserv-tn6530` are reserved. Invalid definitions and conflicting IDs reject the whole package. The example plugin includes **Example byte echo**, a raw-byte echo profile without Telnet negotiation. Loaded providers appear after the three built-in profiles and support plain TCP and TLS. Installation and enable/disable changes take effect after restart. These additive contracts keep API version 2; build against the updated SDK and use the matching updated host. Existing plugins can omit the property. The developer kit includes the new contract source.

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

## Raw ISO 8583 messages and jPOS XML

**Parsers → ISO 8583 Parser** accepts one pasted raw message in ASCII text or whitespace-separated hex. Select a packager and Parse. Results include field number, the XML `name` description, decoded value, raw hex (including variable-length prefixes), and zero-based byte offset. Incomplete messages retain earlier fields and the available portion of the current field, with explicit warnings. Missing definitions, unsupported codecs, invalid lengths and trailing bytes also produce warnings; unparsed bytes remain visible. ASCII mode preserves whitespace exactly. Exclude transport framing, or use the XML root's `headerLength` for a fixed header.

Ship unmodified GenericPackager XML files under `definitions/iso8583/<unique-profile-id>.xml` and list every XML file in `plugin.json` → `files`. Add `<None Update="definitions/**/*.xml" CopyToOutputDirectory="PreserveNewest" />` to the plugin project. Enabled, successfully loaded packages contribute profiles at startup; no new SDK export or Java dependency is required. The filename without extension is the profile ID/name and must be unique across user and plugin profiles. Duplicate IDs are skipped with a startup diagnostic. Users can also put XML in `%LOCALAPPDATA%/ProtoBench/definitions/iso8583/` and reload, or use **Load jPOS XML…** for the current tab. No protocol profile is bundled into the app by default.

The example plugin includes the synthetic `example-ascii` profile. Parse `020060000000000000000512345000000` to inspect MTI, bitmap, synthetic account `12345`, and processing code `000000`. Remove the last three digits to demonstrate partial output. The same XML can be loaded directly from `samples/plugins/ExamplePlugin/definitions/iso8583/example-ascii.xml` without installing the plugin.

This is a basic declarative reader, not a Java/jPOS runtime. Supported field classes use the exact `org.jpos.iso.` prefix:

| Classes | Representation |
| --- | --- |
| `IFA_NUMERIC`, `IFA_AMOUNT`, `IF_CHAR` | Fixed ASCII text |
| `IFA_LLNUM`, `IFA_LLLNUM` | ASCII length prefix and numeric text |
| `IFA_LCHAR`, `IFA_LLCHAR`, `IFA_LLLCHAR`, `IFA_LLLLCHAR` | 1–4 ASCII prefix digits and text |
| `IFB_NUMERIC`, `IFB_LLNUM`, `IFB_LLLNUM` | Packed BCD values; variable lengths use BCD prefixes; odd digits honor `pad` |
| `IFB_FNUMERIC`, `IFB_FLLNUM`, `IFB_FLLLNUM` | Packed BCD with right F padding, or leading zero nibble when `pad=true` |
| `IFB_HEX` | Packed hex characters with odd-length padding; D remains D (numeric BCD displays D as `=` for track 2) |
| `IFB_AMOUNT` | Literal sign byte followed by BCD digits; XML length includes the sign |
| `IFB_LLCHAR`, `IFB_LLLCHAR`, `IFB_LLLLCHAR` | BCD length prefix and ASCII text |
| `IFA_BINARY` | Fixed ASCII hex; XML length counts decoded bytes |
| `IFB_BINARY` | Fixed literal bytes |
| `IFA_LBINARY`, `IFA_LLBINARY`, `IFA_LLLBINARY`, `IFA_LLLLBINARY` | 1–4 ASCII prefix digits and literal bytes |
| `IFB_LLBINARY`, `IFB_LLLBINARY`, `IFB_LLLLBINARY` | BCD length prefix and literal bytes |
| `IFE_CHAR`, `IFE_NUMERIC`, `IFE_AMOUNT` | Fixed EBCDIC text, including sign for amount |
| `IFE_LCHAR`, `IFE_LLCHAR`, `IFE_LLLCHAR`, `IFE_LLLLCHAR`, `IFE_LLNUM`, `IFE_LLLNUM` | EBCDIC prefix and EBCDIC text |
| `IFE_BINARY` | Fixed EBCDIC hex; XML length counts decoded bytes |
| `IFE_LBINARY`, `IFE_LLBINARY`, `IFE_LLLBINARY`, `IFE_LLLLBINARY` | EBCDIC prefix and literal binary bytes |
| `IFAE_LLCHAR`, `IFEA_LLCHAR` | ASCII prefix + EBCDIC text / EBCDIC prefix + ASCII text |
| `IFA_BITMAP`, `IFB_BITMAP`, `IFE_BITMAP` | ASCII hex / literal / EBCDIC hex bitmap; XML length 8, 16 or 24 |

For CP500 hex messages, select **Hex bytes** and **CP500 (EBCDIC International)**. The XML `class` determines each field's representation; the code-page setting applies only to EBCDIC text, prefixes and encoded-hex fields/bitmaps. It does not reinterpret ASCII classes or translate literal binary bytes. **IBM1047 (jPOS default)** preserves jPOS's normal EBCDIC behavior; CP500 must be selected explicitly because punctuation differs. Source hex, padding and prefix bytes remain available unchanged. The synthetic `example-ebcdic.xml` sample can be loaded directly or supplied by the example plugin. A CP500 sample with account `12345` and processing code `000000` is `F0F2F0F0F6F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F0F5F1F2F3F4F5F0F0F0F0F0F0`.

Field 0 is the MTI and field 1 the bitmap. XML bitmap length 8 permits primary only, 16 permits primary/secondary (DE65 remains data), and 24 permits a tertiary bitmap (bit 65 becomes the continuation indicator). Set `maxValidField="192"` for fields beyond 128. Root attributes `headerLength`, `maxValidField`, `emitBitmap="true"`, `bitmapField="1"` and `firstField="2"` are accepted. Field `pad` and `trim` attributes are accepted. Unpacking preserves leading numeric zeros and character padding, as jPOS's string unpackers do; `pad` controls the side of an odd BCD padding nibble. A truncated fixed field remains labelled incomplete even with `trim=true`.

Composite/subfield packagers, custom Java classes, other unlisted classes/attributes, and nonstandard bitmap/root layouts remain unsupported. Unsupported field definitions stop decoding when encountered, preserving earlier results. Unsupported root settings reject the profile rather than assume offsets. XML DTD declarations are tolerated but ignored; external resources/entities are never resolved. XML and raw messages are each limited to 1 MiB. Values remain protocol strings/hex; no currency scaling, code lookup or full jPOS validation is inferred. The examples and encoding/prefix strategies in the [jPOS field-packager tutorial](https://jpos.org/docs/tutorial/iso-packager/field-packagers/) are supported; this does not imply execution of arbitrary Java packagers or compatibility with every jPOS XML layout.

The reader's codec choices follow the public [jPOS field packager implementations](https://github.com/jpos/jPOS/tree/main/jpos/src/main/java/org/jpos/iso); in particular variable binary classes carry literal bytes, unlike fixed `IFA_BINARY`, which carries ASCII hex. Plugin developers can call `Iso8583Profile.FromXml(xml, id)` and `Iso8583Parser.Parse(profile, bytes)` from `ProtoBench.Parsing`. The existing two-argument overload defaults to IBM1047; use `Iso8583Parser.Parse(profile, bytes, Iso8583EbcdicEncoding.Cp500)` for CP500. The developer kit includes these additive APIs and the explicit codec mappings. Rebuild against the updated kit to use them.

The results view presents a tree with the decoded value beside each field description, without a duplicate value row. Each field has a selectable **Source value** child and a **Wire details** branch containing the length prefix, full source field, raw hex and byte range. Source value is the exact value portion of the pasted input, excluding its length prefix: an ASCII MTI `0200` shows source `0200`; an LL field `0512345` shows value source `12345`, prefix `05`, and full field source `0512345`. In hex input mode the source retains the original hex case and whitespace between its first and last digit. **Expand all** opens every branch; **Collapse all** leaves field descriptions and decoded values visible; **Expand level 1** opens fields while closing their wire details. New results start at level 1. Value byte ranges are zero-based with an exclusive end; incomplete fields expose only their available source bytes. **Full result as text** and **Copy result** retain all details for working backwards through a message.

## ISO 8583 message builder

Input fields appear as accordion headers and start collapsed, showing a short value/format summary. Click a header to edit that field; opening it closes the other field editors. Adding another field preserves the current editor state and leaves the new field collapsed. The hex and ASCII preview editors share the available pane height and grow with the window; each has a minimum usable height and its own text scrolling. The packed-field inspection list remains available below them.

**Generators → ISO 8583 Message Builder** uses the same startup-loaded user/plugin XML profiles as the parser, with **Load jPOS XML…** for a tab-local profile. Select a spec, enter the MTI, choose data fields with **Add field**, and enter each value. **Build preview** generates the bitmap and variable-length prefixes, packs fields in numeric order, and parses the result back before showing it. The preview includes lossless full-message hex, a printable ASCII view with non-printable bytes escaped as `\xNN`, and an expandable list of packed fields with encoded bytes and offsets. **Copy hex message** copies the exact bytes as hex; **Copy ASCII message** is available only when every byte is printable ASCII.

Each field supports **ASCII text value** or **Hex (encoded value bytes)**. Text supplies the logical value: `12345` becomes BCD when its XML class requests BCD, or EBCDIC text for an EBCDIC class. Hex supplies the already-encoded value bytes, **without** a length prefix: an ASCII `0200` MTI is `30323030` in hex mode, a packed BCD MTI is `0200`, and literal binary data `00FF` stays those two bytes. Fixed numeric text is left-zero-padded, fixed character text is right-space-padded, and binary values must have exactly their XML fixed length. Fixed `trim=true` text fields require their complete configured length. ASCII text entered for a binary field is treated as ASCII payload bytes before the binary packager is applied. For variable BCD hex, the optional digit count distinguishes an odd digit count from a padding nibble; blank counts all nibbles. For example, `123450` with digit count `5` builds prefix `05` and decoded value `12345` using `IFB_LLNUM` with right padding.

The EBCDIC code-page selector supports IBM1047 and CP500, applying only to XML EBCDIC field encodings. A profile with `headerLength` requires an exact header supplied as hex. Bitmap field 1 and tertiary indicator field 65 (when applicable) are generated rather than editable. Duplicate/unknown fields, invalid text/hex, invalid numeric/amount values, unsupported classes, excess lengths and insufficient bitmap capacity produce errors without leaving an old preview visible. Editing any field, header or code page invalidates the previous preview; changing spec or **Clear fields** resets to the default MTI (`0200` for four-digit MTI profiles). Inputs remain in the tab and are not written to settings. The builder adds no transport framing and calculates no MAC.

Plugin authors can call `Iso8583Builder.Build(profile, inputs, encoding, headerHex, cancellationToken)` from `ProtoBench.Parsing`, supplying `Iso8583BuildField` entries with text or encoded-hex values. This uses the same supported codec set as the parser and retains the same custom/composite-packager limitations. An unsupported codec blocks only when selected for the message. The developer kit includes the builder source/API.

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
    Specification = "Synthetic status example (demo data only)\n\n"
        + "Code  Meaning\n"
        + "────  ──────────────────\n"
        + "A1    Demo accepted\n"
        + "B2    Demo rejected"
};
```

The host transfers this text to `ParsedField.Specification` and displays it in a selectable, read-only monospaced block with horizontal and vertical scrolling and no wrapping. Spaces and line breaks are preserved; Markdown and HTML are not interpreted. Use spaces for column padding and insert paragraph line breaks yourself. The example above is synthetic and does not reproduce any protocol specification. The plugin owns the content, headings and table layout; do not include the surrounding panel or tab labels. Complete parsers can set `ParsedField.Specification` directly. Empty or omitted text shows “No specification supplied for this field.” Existing plugins can omit this additive property; the API version and existing constructors are unchanged. Rebuild plugins using it against the updated SDK and run them with the updated host.

The `spec-to-tool-config` skill can transcribe field descriptions and target known installed decoder IDs. New decoder algorithms and custom tools require developer code; the skill must not invent decoder IDs merely to make a spec appear covered.

## Verification

`dotnet build ProtoBench.slnx` checks both portable and WinUI SDKs. `dotnet test ProtoBench.slnx` covers import rollback/path validation, incompatible API versions, disabled plugins, duplicate IDs, isolated DLL loading with shared contracts, tool execution, CRLF parser offsets, decoder/description flow, unavailable decoders, and independent session cancellation/disposal. For UI verification, import a sample, restart, open its tool through sidebar/dashboard/new-tab, run it in two tabs, and check its parser/description/decoder in Log Parser.

On a Windows desktop with WebView2 Runtime installed, run `./scripts/Test-PluginWebView.ps1` for the native HTML-screen regression check. It renders the SDK counter sample, checks its session bridge, verifies blocked external/data-URL navigation leaves the screen usable, and checks Unicode HTML and independent sessions across screen switches. It writes a JSON report, screenshot and isolated browser profile under `artifacts/webview-smoke/`. This is a standalone native check; it is not part of the portable `dotnet test` suite. The host must allow the exact encoded document URL used by `NavigateToString` during startup even though the loaded document/message source is `about:blank`.


