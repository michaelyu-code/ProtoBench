# Term log parser

Open **Log Parser → Paste text**, paste complete messages, and choose **Parse text**. The **Open file** tab accepts a path or file picker. Both inputs use the same detection, parsers, inspector and diagnostics. Paste is interpreted as UTF-8, preserves source whitespace, and never adds to recent files. Paste is limited to 16 MiB.

Term profiles are supplied by extensions or custom user definitions. The generic record engine reads multiline `RECORD … FROM … IN|OUT ON … SEQ:` dumps using the installed profile. Select an installed profile manually if several compatible profiles are detected. The year and timezone are unknown; date sorting uses month/day/time without claiming an absolute timestamp.

## Analysis

The field-details panel has a draggable divider when displayed beside the field list. Drag left to widen details or right to narrow them. Focus the divider and use Left/Right for 16px adjustments, or Home to restore the default. The chosen width is retained within the current parser tab across field/message changes and temporary narrow-window stacking; it is not saved across app restarts. Minimum widths keep both panes usable.

- Search all logged text or enter a field number, canonical display name, or configured label to restrict a value search. An empty search with a field selects messages containing that field.
- Filter direction and request/reply independently. Sort by record number, logged date/time, STAN, acceptor ID, terminal ID, sequence or MTI, ascending or descending. IDs retain leading zeros.
- The field details contain extension-provided decoded fields and built-in ICC tag entries. ICC `TAG:HEX-BYTE-LENGTH:VALUE/` input is unwrapped before decoding. Only common EMV profiles are automatically interpreted. Tags without an automatic decoder, including those needing a scheme profile, remain plain tag/value entries without a Breakdown child. Scheme profiles can be selected separately in EMV Tag Decoder. Duplicate and malformed tag entries remain visible. An ICC/DE4 amount difference is a review finding, not an automatic protocol failure.
- Validation flags mandatory fields missing **from the log**, empty required values, format errors, duplicates, unknown labels and explicitly forbidden conditional fields. The rule ID includes the profile, MTI and DE. Unresolved conditions are marked **Undetermined**, including conditions requiring entry-mode definitions or prior transaction context. A missing log field does not prove absence on the wire.
- Related messages are candidates sharing the profile and all configured `CorrelationKeys`, with opposite directions and compatible configured MTIs within `CorrelationWindowSeconds` (default 300). Keys default to `["11", "41"]` (STAN + terminal). Each key must be present and equal. `CorrelationSameConnection` defaults to `true`; a profile can set it to `false` for requests and replies logged on different links. For profiles using STAN + transmission date/time, set `CorrelationKeys` to `["11", "7"]`; DE41 is then not required. DE7 does not replace the logged-time window. All candidates remain visible when ambiguous. The missing year prevents reliable cross-year correlation. This does not perform automatic reversal lifecycle matching.
- The detail panel displays decoded subfields in aligned **Field** and **Value** columns, with wrapped values, descriptions and indented nested rows. All decoded levels are visible without expanding a tree. Selecting a subfield keeps the detail panel open. Use **View source** or right-click **View in original message** to highlight its value in yellow in the Original view. ICC decoding preserves individual value source ranges; extensions supply source ranges for their decoded fields.

## Profiles and versions

Synthetic engine-test rules live in `tests/Fixtures/definitions/messages/synthetic-record-v1.json` and are not shipped with the app. Install profiles through extensions or place custom profiles in the user's `definitions/messages` directory. Restart to load changes. Give each spec/version a unique ID; duplicate IDs are rejected with startup diagnostics.

`SchemaVersion` versions the configuration schema; `SpecVersion` identifies the protocol specification. Protocol definitions, operation meanings and transaction/account decoding belong to extensions. No vendor protocol profile is bundled with the app.

`Fields` defines stable IDs, display names, label aliases, logged types (`text`, `digits`, `hex`), fixed/maximum lengths and optional registered decoders (`emvEntries` or an extension-provided ID). Add every multiword label explicitly: unknown single-token and `Fld N` labels are retained, but unconfigured multiword labels cannot be reliably distinguished from free text. Values containing label-like `name=` text are inherently ambiguous in this dump layout; original text is always retained.

`Messages` defines each MTI's name, role, compatible replies and field rules:

Fields may also carry `Description`, displayed in the field detail panel. An enabled developer plugin can provide additional `Decoder` IDs for subfield parsing and code meanings; missing decoders retain the raw value with an Undetermined finding. See [developer plugins](PLUGINS.md) for SDK contracts, import and examples.

```json
{
  "Field": "47",
  "Usage": "C",
  "WhenField": "3",
  "WhenValues": ["20"],
  "WhenPrefix": true,
  "ForbidOtherwise": true,
  "Comment": "Refund only."
}
```

Usage supports M (mandatory), O (optional), C (conditional), F (forbidden). A conditional rule without an executable predicate remains unresolved and preserves the explanatory comment. Predicates compare full values, or prefixes when `WhenPrefix` is enabled. Set `RequireWhenMatched: false` for applicability-only conditions that do not establish required presence. These matching conditions remain unresolved as to required presence. `Name` and `Length` can override the field display name and length per MTI. Unlisted fields are not automatically forbidden.

Profiles describe logged values. Wire-level encoding, length prefixes and protocol completeness require extension-specific validation.

## Limits

Both imports produce bounded previews: 16 MiB, 10,000 messages, 1 MiB per message, and 2,048 ICC entries per field. An incomplete final message at the byte boundary is excluded. All search and correlation operate on loaded records; truncation is visible. Full-file indexing, a visual profile editor, profile inheritance and exports are future extensions.

Verification: parser tests cover multiline CRLF/BOM input, source byte ranges, wrapping, aliases, extension decoder dispatch and ICC interpretation, malformed entries, missing/forbidden/unresolved fields, search/sorting and ambiguous correlations. Paste workflow tests cover Unicode, source whitespace, manual detection, empty/unknown input, file-to-paste transitions and recent-file isolation. Desktop interaction still needs manual verification.
