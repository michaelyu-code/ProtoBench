# Verification record

## SDK structured tool results — 23 November 2026

Version 2.0.10 adds optional search and field-decoder result contracts to API 2, with shared WinUI host rendering. Search presents ordered hits, excerpts, a selected passage, literal term emphasis, source labels and collapsed ranking/raw details. Decoding presents status, raw value, summary, labeled component/value/meaning rows, expandable children, warnings, context, references and copy actions. Integer and supporting-field inputs, optional input groups, and input-edit invalidation are available to standard portable plugins. Existing text/tree tools and custom WinUI providers remain supported.

Release solution and native UI builds passed with zero warnings/errors. The full suite passed 556 tests with the existing opt-in 2-GiB test skipped. Fifteen new cases cover structured results with empty raw output, clear/edit/disposal invalidation, mixed-presentation rejection, numeric bounds and fractional input, invalid/duplicate supporting fields, exact value preservation, cancellation, and loading/executing the standalone structured-results plugin against shared host contracts.

The complete developer kit (matching app and SDK) was built at `artifacts/developer-kit-2.0.10.zip`. All three standalone sample projects compiled and packaged successfully; SDK/app DLL hashes matched. The kit includes the new contract source, guide and synthetic search/decoder sample. The sample does not implement BM25 or real specification mappings. An isolated native visual-test harness failed to expose a targetable window, so live appearance, narrow-window behavior, keyboard selection and clipboard actions have not been visually verified. No installed plugins or user settings were changed. The HTML design previews remain local and are not release source.

## Manual sidebar expansion — 22 November 2026

The title-bar menu toggle now overrides responsive Auto mode for the current session. Opening it docks the pane; selecting a category/tool and resizing no longer dismiss it. Explicit collapse uses the compact rail or hides the pane at narrow widths. Menu-container replacement is queued after pane transitions instead of mutating NavigationView during its property callback.

Release solution build passed with zero warnings/errors. Live checks confirmed opening from compact mode, selecting Utilities, keeping the pane docked while narrowing through both responsive breakpoints, explicitly collapsing/reopening below 640 DIPs, and opening the relay with the sidebar still visible. The verification copy was closed; the original Debug session was preserved.

## Timed Key Presser — 22 November 2026

Added the Utilities workspace with key selection (F1–F24 and common keys), an interval in seconds, combined Start/Stop, locked configuration and a bounded timestamped auto-scrolling activity textbox. The UI-thread timer continues across tab switches; workspace disposal stops it. Native SendInput emits a down/up pair, checks held modifiers/keys, and reports rejected or incomplete input. Scheduling uses monotonic elapsed time and does not replay overdue presses after a pause.

Release build passed with zero warnings/errors. The suite passed 517 tests with the existing opt-in 2-GiB test skipped. Fifteen new cases use a fake clock and fake keyboard without injecting real input: validation, first-press delay, repeated intervals, input locking, stop/restart/disposal, late ticks, wall-clock changes, held-key skips, failed delivery, catalog validation and log retention.

Live Release checks opened the tool through dashboard search, confirmed F24/300 defaults, changed to one second, observed real accepted F24 presses and timestamps, disabled inputs and the Stop label, then stopped and confirmed inputs unlocked. A second run continued while Dashboard was selected; returning showed the increased count and log scrolling to later entries. The session was stopped with 53 total presses. The verification window was left stopped. Delivery to elevated targets, Windows suspension and alternate DPI/theme configurations remain manual.

## Category dashboard navigation — 22 November 2026

Reproduced opening TCP / TLS Relay and clicking Utilities in the collapsed sidebar: the dashboard returned to All tools and highlighted Home. Dashboard category filtering now keeps its own state across tab unload/reload, restores the dropdown on load, and synchronizes the sidebar with the active category. Selecting a category activates the Dashboard tab before applying its filter; the page heading identifies the section. Explicit Home navigation still resets to All tools.

Verified in a separate Release instance while preserving the running Debug session: Utilities showed exactly TCP / TLS Relay, Test Telnet Server and X.509 Certificate Inspector after returning from the relay tab, with Utilities highlighted; Home showed all 13 tools; Payments showed its four tools. The verification copy was closed afterward.

## Responsive workspaces — 22 November 2026

Added wrapping action rows, compact page spacing, bounded output editors, coordinated navigation breakpoints and an open-tabs menu. Standard narrow tool workspaces switch between Inputs and Results without recreating editors. Parser/relay details use full-width navigation; parser field details no longer force a 560/720-DIP minimum viewport. Generator field editing preserves the JSON representation. Settings and dashboard layouts were compacted, with per-tool result improvements for calculators, payments and certificates.

The Debug solution builds with zero warnings/errors. The four test projects pass 502 tests with the existing opt-in 2-GiB test skipped. The initial sandboxed test run could not connect to testhost; running the same command outside that sandbox completed successfully. Parser presentation tests now verify that opening details preserves the existing viewport budget.

Live checks covered dashboard reflow in narrow/short windows, keyboard search/Enter in the new-tab picker, bitwise calculation and automatic result navigation, generator field population and a 16-byte preview, relay configuration access and field validation, grouped Settings, and EMV example loading/byte selection across narrow and wide layouts. The EMV check caught a visual-parent exception; byte editors now remain in one container and switch visibility. Repeating the check preserved E0F8C8 and Byte 2/F8 through both resize directions without crashing. No listeners were started or files exported during these checks.

Broader verification remains manual: enlarged Windows text and alternate DPI/theme combinations, parser navigation with installed format definitions, relay inspection with active traffic, custom plugin views, and native file dialogs. Recommended window sizes are 1280×800, 1024×768, 800×600 and a narrow snapped window. Check primary actions, keyboard focus, Back navigation, retained input/selection, and long field/error text at each size.

## Relay payload tab alignment — 16 November 2026

Moved the capture summary into the left traffic pane so the right Hex/Text/Details tabs start at the top of the shared card in the side-by-side layout. Summary text can use four lines within the narrower traffic pane. The existing narrow-window stacking and footer actions are retained. The tab alignment was visually checked in the running app; the Desktop build passed with zero warnings/errors.

## Independent upstream trust and validity-date checks — 16 November 2026

Added Verify upstream certificate trust and Verify upstream validity dates alongside hostname verification, all enabled by default. Unknown-CA/partial-chain and date errors can be relaxed independently through chain-policy flags; other certificate/TLS errors remain enforced. Connection events record all three settings. Nine real-socket test cases cover trusted/untrusted, expired/not-yet-valid and wrong-name combinations, including successful traffic when the relevant checks are disabled and rejection when an unrelated enabled check fails. The full suite passed 425 tests with one existing opt-in test skipped; the Desktop build passed with zero warnings/errors.

## Relay upstream matching and hostname control

Added Upstream connection choices Plain TCP, TLS and Match incoming, plus Verify upstream hostname (default on). Matching follows each incoming connection's actual transport; upstream SNI uses an explicit override, then client SNI in Match mode, then the configured upstream host. Endpoint routing remains fixed and TLS versions/ciphers negotiate independently. Real-socket tests cover mixed transports, client-SNI reuse, missing-SNI fallback and an override with a different upstream certificate name. Disabling hostname verification allows a name mismatch with a trusted valid certificate while still rejecting untrusted and expired certificates. The full suite passed 416 tests with one existing opt-in test skipped; Desktop builds passed with zero warnings/errors. The new upstream dropdown, Match incoming selection and checked-by-default hostname control were visually verified in the running app.

## Relay viewport, automatic TLS and hostname diagnostics — 15 November 2026

Replaced the relay's scrolling page with bounded grid rows and independent traffic/payload panes, enabled stretching in the shell and payload TabViews, compacted the toolbar and added height-aware connection-panel collapse. The layout was visually checked at the normal 1320×900 startup size and in a temporary 840×640 QA build; traffic and the hex/text inspector stayed in the same viewport, with connection settings collapsed in the short window. The QA-only startup-size change was reverted. Incoming Auto mode peeks without consuming the first byte, selecting TLS for `0x16` and plain TCP otherwise, and generates a test certificate if needed. Upstream certificate errors now report the checked name, subject/SAN and SNI/trust guidance while retaining strict validation. The full suite passed 409 tests with one existing opt-in 2 GiB test skipped, including mixed plain/TLS clients on one port, preserved first bytes, empty/silent clients, stop during peek, automatic certificate creation and useful name-mismatch diagnostics. Desktop builds passed with zero warnings/errors.

## Relay layout — 15 November 2026

The relay now places the full-width Connection section above the full-width Traffic section. Listen and upstream settings sit beside each other within Connection and stack on narrower windows using the existing adaptive layout. The revised layout was visually checked in the running app; the Desktop build passed with zero warnings/errors.

## TCP / TLS relay — 15 November 2026

Added a Utilities workspace with independent incoming/upstream TLS, PFX/P12 loading, test-certificate creation and public certificate export, upstream trust/name validation, a bounded traffic ring, hex/UTF-8 inspection and JSON Lines export. Real loopback integration tests cover all four plain/TLS leg combinations with binary payloads, concurrent clients, plain TCP half-close, trust/name rejection, connection limits, setup timeouts, shutdown/port release, capture eviction and workspace/PFX/export lifecycle. The full suite passed 406 tests with one existing opt-in 2 GiB test skipped. Desktop builds completed with zero warnings/errors. Windows UI automation opened the workspace, started/stopped it, confirmed configuration locking, relayed `Hello` and received `ACK:Hello`, observed both capture directions and byte counts, and checked `41434B3A48656C6C6F` / `ACK:Hello` in the hex/text inspectors. Test-certificate generation displayed its SHA-256 fingerprint and expiry. No machine trust or firewall settings were changed. The file-picker export dialogs and remote/LAN clients have not been exercised interactively.

## Collapsed sidebar category selection — 15 November 2026

The collapsed navigation rail uses direct category entries so a category click filters the dashboard without opening a child flyout. The expanded sidebar retains its nested tool entries. Windows UI automation reproduced the original problem: with a child flyout open, the first click on another category only dismissed it. In the revised running build, clicking Converters and then Calculators once changed the dashboard from one converter to three calculators. Expanding the sidebar and Converters restored the Base Converter child entry. The Desktop build completed with zero warnings and errors.

## Adjustable field-details width — 8 November 2026

Added a 12px draggable native Thumb divider between field list and details, a horizontal resize cursor and hover/pressed grip feedback. Left/Right keyboard controls adjust by 16px; Home restores the automatic default. Width is retained per parser view across field/message changes and narrow-window stacking, and clamped to keep at least 180px for fields and 220px for details. The divider is hidden for stacked or closed details. The normal Debug solution builds with zero warnings/errors. Dragging, keyboard resizing and focus appearance still require an interactive smoke check; no backend behavior changed.

## Compact message cards and field side panel — 8 November 2026

Messages now occupy a 240px column beside the record inspector. Each selectable card groups record/direction, MTI/role, time, STAN/sequence, terminal, acceptor and a check summary. Search remains immediately available, while filter/sort controls are in an expander. Validation uses red violations, yellow warnings/unresolved checks and green no-findings indicators, with descriptive text/tooltips and a legend. Palette variants support light/dark themes and foreground text replaces status colors in high contrast. Selecting a field opens a closable details panel beside the field list; narrow layouts stack panels. Copy, source navigation, full values and decoded trees remain available.

The Release preview compiled successfully and was inspected in the running app at approximately 1043 × 714. The four-message sample rendered as compact cards beside the inspector with green/yellow status dots. Selecting a field opened its right-side details panel with Copy/View source actions; the field list remained visible. The final normal Debug solution builds with zero warnings/errors. Final wording, empty-details spacing, high-contrast handling and narrow-layout corrections compile but have not had a separate interactive recheck. Light/high-contrast appearance, resizing and red-violation rendering remain manual checks. Parsing rules are unchanged.

## Message browsing and inspector usability — 8 November 2026

Replaced the two cramped side-by-side panels with a full-width message table and an inspector below it. Column headings sort; selection persists across sorting/filtering when the record remains visible. Previous/Next buttons complement keyboard row navigation. The input area collapses after successful parsing. Inspector fields use separate DE/name/value/status columns, with missing required fields represented as rows. Selecting a field opens its complete value, copy/source actions and decoded children; source offsets are moved to decoded-node tooltips. Related-message candidates are clickable and clear conflicting filters when necessary. Required errors are distinguished from unresolved checks. Accessibility row text describes content rather than exposing managed type names.

A separate Release preview build was exercised with the four-message fixture. All four messages rendered in aligned columns; selecting 18223 updated the inspector to 0200 Request. Its field table displayed names and values, and selecting DE3 exposed `001000` with Purchase/Savings decoded children and copy/source controls. The final title-spacing, accessible row descriptions, source-newline conversion and bring-into-view adjustments compile in the normal Debug solution build with zero warnings/errors. Those final small adjustments and the new copy/source/related-navigation actions have not had a separate end-to-end UI recheck. Backend parsing and validation rules are unchanged.

## Term paste boundaries and field tree repair — 8 November 2026

Fixed the header matcher to accept CR-only (WinUI editor), LF and CRLF boundaries without normalizing source text. Logical line numbers count each form correctly. Synthetic message regressions now check MTIs, record IDs, line numbers, raw reconstruction, field byte offsets and ICC entries across all three newline styles. All 26 parser tests pass.

Added an explicit TreeView item template binding each node's Content rather than allowing the control to render the TreeViewNode type name. A separate Release preview build succeeded with zero warnings/errors. Using the running desktop preview, the four-message fixture was pasted into the Log text editor and parsed with automatic detection. The UI displayed **4 of 4 loaded records**, MTIs 0800/0810/0200/0210, and actual field labels/values (Message type, Primary bitmap, Processing code, STAN, etc.) in the inspector. This verifies the repaired paste and field-display path that was previously only covered by library tests. Nested ICC expansion, field-source selection and additional layouts remain candidates for manual checks.

## Configured Term log analysis and pasted input — 8 November 2026

Release solution build passes with zero warnings/errors. All 264 tests pass (Utilities 211, Application 18, Parsing 25, Generation 10). New coverage exercises multiline message boundaries and metadata, CRLF/BOM/UTF-8 source offsets, configurable aliases, padded values, wrapped ICC entries and decoder summaries, malformed/duplicate/unknown fields, missing/forbidden/undetermined requirements, applicability-only conditions, search/filter/sort, ambiguous request/reply candidates, and paste/file transitions with recent-file isolation. UI now offers Paste text and Open file tabs, search/filter/sort controls, validation and related-message inspectors. The desktop build is verified; the new interaction/layout has not been manually smoke-tested. See `TERM-LOG-PARSER.md` for scope, remaining spec gaps, and bounded-preview limits. This supersedes the older statement below that no real NonStop format has been implemented; the new Term profile is an initial implementation, not a complete wire-protocol validator.

## Feature-flagged updates (8 November 2026)

Update checks, download-page fallback and Velopack download/restart support are implemented with `UpdatesEnabled=false` by default. Release builds pass with zero warnings/errors and all 217 tests pass. Ten new update tests cover disabled service guards with no HTTP/backend access, numeric semantic-version ordering, equal/older versions, malformed/prerelease manifests, HTTPS configuration validation, installed versus ordinary folder-copy routing, failed downloads, and explicit restart prerequisites. An enabled build without endpoint properties is rejected by the configuration validation target as expected. The default Release executable started successfully in a process smoke check and was then stopped. Settings controls, browser launching and a real two-version Velopack upgrade still need manual release verification. See `UPDATES.md` for activation and release preparation.

Environment: Windows x64, .NET SDK 10.0.302, Windows SDK 10.0.26100. Validation performed 7 November 2026 (Pacific/Auckland).

## Automated checks

```powershell
dotnet restore ProtoBench.slnx
dotnet build ProtoBench.slnx --no-restore
dotnet test ProtoBench.slnx --no-build --no-restore
```

The full Debug solution build passes with zero warnings and zero errors. Following the ASCII/hex and CP500 additions on 8 November 2026, all 95 xUnit tests pass across four projects:

| Project | Tests |
| --- | ---: |
| Utilities | 65 |
| Parsing | 15 |
| Generation | 10 |
| Application / infrastructure integration | 5 |

Fixture-based parsing reads the shipped synthetic files. Generation compares exact bytes against the shipped binary fixture, including padding and CRLF. Additional checks exercise ambiguous detection, invalid rules and encoded data, transformed field source ranges, BOM/CRLF offsets, record/byte/line limits, cancellation, duplicate IDs, arbitrary-precision numeric conversion, settings persistence, workspace disposal, and preservation of existing files when a staged write fails.

ASCII/hex tests verify exact bytes, mixed-case and whitespace-separated hex input, whitespace/control preservation, the complete 128-character ASCII range, empty input, invalid digits/pairs, non-ASCII rejection and size limits. The new conversion selector compiled successfully; its interactive behavior has not yet had a separate desktop smoke check.

CP500 tests compare decoding of all 256 byte values against `samples/cp500-codepoints.txt`, an independent fixture generated with Python's standard-library `cp500` codec, and verify exact encoding back to the original bytes. Known vectors cover letters, numbers, CP500-specific punctuation, extended characters and controls. Additional tests cover ASCII byte transcoding, invalid hex, size limits, and rejection of unrepresentable text (including the Euro symbol and malformed surrogates). The CP500 selector entries and DI wiring compile; a separate desktop smoke check of those entries remains outstanding.

## Desktop smoke checks

The actual unpackaged WinUI executable was launched and inspected using Windows computer-use tools:

- Dashboard and categorized navigation render.
- Converter accepts the default decimal `255` and produces hexadecimal `FF`.
- Converter and generator tabs coexist; generated preview contains 16 bytes, including three spaces after `ALPHA`, zero padding for `42`, and final `0D 0A`.
- Parser workspace opens and remains stable after its templated controls were moved into XAML.
- A local path to `samples/synthetic-delimited.txt` parses with automatic detection, yielding two records (`hello`/42 and `café`/7), raw text and field source ranges. Status reaches Completed.
- The native file picker opens. Automated selection within PickerHost was not completed because the computer-use helper could not bind that separate host window; the path input completed the end-to-end parser check.

The application must run in the normal interactive Windows session; sandboxed launches exited before presenting a window. Builds and library tests ran in the workspace. Two desktop output paths are possible depending on whether a project or the solution is built; the documented run script uses the explicit x64 path.

## Remaining manual checks

### EMV tag/profile expansion — 8 November 2026

The EMV catalogue now contains 13 tags and 19 explicit tag/profile combinations; coverage and version limits are recorded in `EMV-PROFILES.md`. This adds coded-field summaries, Visa FFI v1, Mastercard Third Party Data, American Express Kernel 4 v2.10 variants, Discover Kernel 6 v2.7 TTQ, CVM List/Results, CID, Terminal Type, AUC and Additional Terminal Capabilities. Profile-aware byte editing preserves raw values and does not infer unsupported version meanings. Common-only TLV decoding leaves ambiguous scheme-specific tags raw; an explicit context selects only matching supported profiles.

The full Release solution builds with zero warnings/errors. All 251 tests pass (Utilities 211, Application 15, Parsing 15, Generation 10). The 34 new utility cases cover catalogue uniqueness, exact bit positions, Visa form factors and unknown versions, Mastercard ASCII device codes/optional device structure, Amex/Discover differences, CVM rule thresholds/order, CID/terminal codes, invalid profile/length combinations, bit round trips across all editable profiles, and explicit versus ambiguous TLV contexts. Sandboxed test runs could not connect to their local testhost processes; the suite completed successfully outside the sandbox.

The referenced site's Visa and Mastercard demo workflows were inspected. In the running preview, the expanded tag list and three distinct 9F6E profiles appeared. Switching from Visa to Mastercard and loading its example produced `02800000323100`, country code `0280`, identifier `0000`, device `3231 (21)` / Mobile phone, proprietary data `00`, and seven correctly sized byte tabs. The layout was visually inspected. Additional profiles, profile-switch errors, byte edits after scheme changes, narrow-window layout and clipboard remain candidates for broader UI testing; the service cases above cover the underlying interpretations.

### Tool layout revision — 8 November 2026

All ten tool workspaces were updated through shared responsive panels: compact input/result columns, related option rows, collapsed help, bounded text editors, and collapsed empty messages/inactive progress. EMV has a full-width byte editor with responsive bit columns and expandable decoded text. Generator text/hex previews and parser field/source inspectors use tabs. The final full Release solution build passes with zero warnings/errors, and all 207 existing tests pass; this change does not alter calculation services.

Running previews were inspected at approximately 1043 × 714 pixels. The 3DES input controls, KCV and results fit together without scrolling. EMV input/operation controls, byte tabs and two-column bit controls were visually inspected with `E0F8C8`; a final compacting pass moved the hex summary beside its actions and removed redundant live-decode controls/status. That final spacing-only pass compiled in the full Release build. Generator Preview still produced the expected 16 bytes. Parser sample loading produced two records and populated the selected record's field tree in the adjacent inspector. The native resize attempt did not change the captured window dimensions, so actual narrow-window stacking, alternate DPI and high-contrast checks remain manual. The responsive grid selects column count from available width and keeps existing controls/state when reflowing.

### Calculator section — 8 November 2026

3DES now shows a separate six-hex-digit encrypt-zero KCV, with Calculate KCV and Copy KCV controls. Successful data calculations populate it too, and editing/clearing the key invalidates it. All 207 tests pass (Utilities 177), including the published `0123456789ABCDEFFEDCBA9876543210` → `08D7B4` KCV, equivalent K1/K2/K1 expansion, parity-bit invariance and invalid-key rejection. Existing running Debug/Release processes locked their output folders, so the current desktop was built successfully to `artifacts/kcv-preview` with zero warnings/errors. In that running preview, Calculate KCV produced `08D7B4` with transaction input empty, and clearing the key cleared the KCV. The ordinary Debug desktop build was blocked by file locks; test assemblies built and passed. Copy KCV and the combined data-result/KCV flow still need separate UI checks.

The Release solution builds with zero warnings/errors and all 205 tests pass (Utilities 175, Parsing 15, Generation 10, Application 5). The 52 new cases cover bitwise operations/bases, width masking and 64-bit boundaries, invalid values and shift counts; RFC 4648 Base64 vectors, UTF-8/binary data, URL-safe alphabets, canonical padding/pad bits and limits; and independent three-key/two-key 3DES vectors, CBC chaining, padding and invalid keys/IVs/lengths. The two-key vectors exposed a Windows one-shot provider restriction with 16-byte keys; the service now expands those to K1/K2/K1 explicitly and the known-answer tests pass.

In the running Release WinUI app, the Calculators category and all three tools were observed. Bitwise `F0 AND 0F` produced `00`; switching to XOR produced `FF` / `255` / `11111111`. Base64 encoded `Hello` to `SGVsbG8=`. The public two-key 3DES vector encrypted `21E81B7ADE88A259` to `5C577D4D9B20C0F8`. Switching ECB to CBC exposed the IV input and cleared the stale result. The Clear inputs smoke check was interrupted by concurrent user input before completion. Bitwise layout was visually inspected in dark theme. The three tool tabs coexisted. Broader alternate-mode, clipboard, theme/DPI and accessibility smoke checks remain manual; service behavior has automated coverage.

### Payment card tools — 8 November 2026

Byte-tab visibility was improved with outlined headers, 14px labels, bold selected text, a 3px accent underline and an outlined editor frame. Brushes use theme resources. The new appearance and switching the active highlight from Byte 1 to Byte 2 were visually verified in the running Debug app in dark theme. Explicit accessible tab names preserve the byte/hex labels with custom header visuals. Light/high-contrast appearance remains uninspected.

The interactive editor now uses embedded Byte 1 / Byte 2 / … tabs, with each byte's current hex in its tab label. The Release build passes with zero warnings/errors and all 153 tests pass. In the running Release app, `E0F8C8` populated tabs E0/F8/C8. Selecting Byte 3 displayed its eight flags; unchecking SDA immediately changed the full input to `E0F848`, the tab label to `Byte 3 · 48`, and byte binary to `01001000`, while retaining the Byte 3 selection. This completes an interactive toggle check as well as the byte-tab switch check.

Interactive EMV bit editing was subsequently added for 9F33, TVR, TSI and AIP. The Debug solution builds with zero warnings/errors and all 153 tests pass (Utilities 123). Five new cases verify editing every bit across all four tags, preserving other bits, reversible changes, idempotent setting, known hex masks and invalid positions/lengths. In the running Debug app, entering `E0F8C8` was observed to populate the interactive checkboxes, live decoded result, full hex and byte-1 binary `11100000`. The checkbox layout was visually inspected. The final click smoke check could not complete because the Windows helper repeatedly reported “window bounds changed” after refreshing; click-driven synchronization, reset, mode switching and clipboard still need a complete interactive recheck.

The Release solution builds with zero warnings/errors. All 148 tests pass: Utilities 118, Parsing 15, Generation 10, Application 5. The 53 added payment cases cover Luhn known check digits and invalid input, primary/secondary bitmap positions and continuation consistency, EMV bit positions and reserved bits, nested TLV offsets/duplicates/long lengths, malformed and bounded TLV parsing, ARQC input validation and verification, an independent published PyEMV ARQC vector, and the ANSI X9.19 method-1 retail MAC known-answer vector. Aligned padding-method behavior is also checked. Contact EMV bit definitions follow EMV 4.3 Book 3 Annex C and Book 4 Annex A2; contactless/later-profile meanings are not inferred.

The actual Release WinUI executable was launched and smoke-tested with Windows computer-use tools. The Payments category and all four dashboard cards appear. Independent EMV, Luhn, bitmap and ARQC tabs opened successfully. Terminal Capabilities `E0F8C8` produced its expected bit descriptions; complete number `79927398713` passed Luhn; bitmap `3020000000800000` reported fields 3, 4, 11 and 41; and the public session-key ARQC vector produced `FA624250B008B59A` with “Verification: match.” The key was masked in the accessibility/UI state. Clear inputs removed key, transaction data, expected MAC and result. The EMV workspace was visually inspected in dark theme. Broader DPI/accessibility, clipboard, alternate modes and theme smoke checks remain manual; the library tests cover their underlying calculations.

### Dashboard and navigation revision — 8 November 2026

The revised dashboard was visually inspected in the running WinUI app. It renders a narrower sidebar, two columns of cards at the inspected window size, local search and category filters, and a persistent Dashboard tab. Opening Base Converter, returning to Dashboard, and using New tab created a labelled Base Converter (2) alongside the existing tab. Search was exercised against the CP500 description. The sidebar uses NavigationView's built-in scrolling menu and adaptive layout; a many-tool overflow scenario and alternate window widths still need broader manual verification.

### New-tab picker and integrated title bar — 8 November 2026

Pressing **+** from Dashboard was observed to open a distinct New tab picker with search focused and the available tools listed. Selecting Base Converter created a workspace and removed the picker. Testing exposed a TabView selection reset during picker removal; the implementation now explicitly reselects the new workspace after removing the picker. That final reselection change compiled and passed the existing suite; its final interactive recheck was interrupted by changing desktop input/window geometry and remains to be confirmed.

The integrated title bar was visually verified with matching dark and light caption backgrounds and contrasting window-control glyphs. Light-theme verification was followed by restoring Default appearance. Native caption buttons are retained. Window dragging, snapping, maximize/restore, and inactive/OS theme changes still need broader manual checks. The full solution builds with zero warnings/errors, and all 95 library tests pass; those tests do not cover native title-bar or tab input behavior.

### Converter space-removal option — 8 November 2026

A per-workspace, initially unchecked checkbox removes ordinary input spaces before any conversion, retaining the original text and leaving tabs, line breaks and output untouched. Toggling it invalidates the previous result. The full Release build passes with zero warnings/errors and all 95 existing tests pass. The running Debug instance locked its executable, so its output could not be replaced; Release verification left that session open. This checkbox has not had a separate interactive smoke check, and existing library tests do not exercise its UI binding.

Clipboard integration, native Save dialog/overwrite flow, theme switching, favorite/recent persistence across app restarts, cancellation and tab closing during a large live operation, accessibility at alternate DPI scales, installer/signing, and distribution to a clean Windows machine still need broader manual/release verification. Services underlying file replacement, persistence, validation, cancellation and disposal have automated coverage. No real NonStop format has been implemented or verified.
