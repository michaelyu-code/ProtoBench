# Current configuration contracts

These notes describe schema version 1. Re-read the repository's schema and engine when using the skill; source code takes precedence.

## Term message profiles (`messages`)

Read `docs/TERM-LOG-PARSER.md`, `tests/Fixtures/definitions/messages/synthetic-record-v1.json`, `src/NonStopToolkit.Parsing/Definitions/MessageProfile.cs`, and `Engines/RecordMessageParser.cs` before transcribing a Term spec. The test fixture contains synthetic engine-test rules, not a protocol specification. Parser specs are supplied by extensions or custom user definitions.

Allowed top-level properties:

| Property | Meaning |
| --- | --- |
| `SchemaVersion` | Integer `1` |
| `Id`, `Name`, `SpecVersion` | Nonempty strings; schema version and protocol version are distinct |
| `Fields` | 1–256 field definitions with unique string `Id`s |
| `Messages` | Object keyed by four-digit MTI strings |
| `TransactionTypes`, `Accounts` | Legacy string-to-string maps retained for profile compatibility; not interpreted by the engine |
| `SourceAccountRequiredFor`, `DestinationAccountRequiredFor` | Legacy arrays retained for profile compatibility; not interpreted by the engine |
| `CorrelationWindowSeconds` | Integer 1–86400; engine default 300, an operational choice |

Supply every collection, even when empty. A field has `Id`, `Name`, `Labels` (array), `Type` (`text`, `digits`, `hex`, default `text`), optional positive `Length`, `MaxLength`, optional `Description` for the detail panel, and optional `Decoder`. The built-in decoder ID is `emvEntries`; enabled plugins can register other case-sensitive IDs (at most 128 characters), usually namespaced such as `example.response`. Unknown decoders remain loadable but produce an Undetermined finding when used. Verify a custom decoder's availability and semantics from plugin source/docs; see `docs/PLUGINS.md`. Aliases are case-insensitively unique across the profile, nonblank, at most 80 characters, and cannot contain `=`. Include actual logged multiword aliases explicitly. Generic `Fld N`/`FldN` aliases can fall back to numeric IDs. Unconfigured single-token labels are retained as unknown fields; multiword labels cannot be reliably inferred.

Each `Messages` value has `Name`, `Role` (`request` or `reply`), `Replies` (array of MTIs also defined in `Messages`), and `Fields` (rules with unique `Field` IDs referencing declared fields). `Replies` records candidate compatible MTIs, not guaranteed one-to-one correlation. Use an empty array for replies without compatible outgoing replies.

Rule properties:

| Property | Meaning |
| --- | --- |
| `Field` | Declared string field ID |
| `Usage` | `M` mandatory, `O` optional, `C` conditional, `F` forbidden |
| `Comment` | Context/explanation, default empty |
| `WhenField`, `WhenValues` | For `C`, compare another field against a nonempty string array |
| `WhenPrefix` | Default false; true compares prefixes instead of full values |
| `ForbidOtherwise` | Default false; forbid the field when predicate does not match |
| `RequireWhenMatched` | Default true; false expresses applicability without establishing required presence |
| `Name`, `Length` | Optional per-MTI display-name and positive logged-length overrides |

Semantics that matter:

- Conditional rules without a predicate, or with an absent/empty predicate field, yield **Undetermined**. The engine does not evaluate prose, compound Boolean expressions, previous messages, terminal state or bitmap completeness.
- “Refund only” establishes exclusion elsewhere, but does not establish that every refund contains the field. Use `RequireWhenMatched: false` unless the source independently requires presence. When matched this still reports unresolved required presence.
- If a usage column and footnote conflict, preserve both in `Comment` and the report; use an unresolved `C` pending resolution. Unlisted fields are not implicitly forbidden.
- `Length` and `MaxLength` constrain the normalized logged string's UTF-16 length. Hex values therefore use character counts, not byte lengths. `digits` accepts only ASCII digits; `hex` requires an even count of hex digits and strips whitespace. All values are trimmed. Do not transfer wire LLVAR/LLLVAR prefixes, packed digits, BCD or binary lengths directly into these properties.
- `emvEntries` expects `TAG:HEX-BYTE-LENGTH:HEXVALUE/` entries and removes wrapping whitespace. Its field normally needs `Type: "text"`, since separators are not hex characters. It is not a generic raw BER-TLV decoder.
- The parser obtains MTI from the field with ID `mti`. Existing analysis uses `11` for STAN and `41` for terminal ID; the DE55/DE4 comparison is hard-coded. Use canonical IDs only when the spec has those meanings.
- Transaction/account interpretation and processing-code operation meanings require an enabled extension decoder. The engine does not assign protocol-specific meanings.
- The engine reads UTF-8 labelled RECORD dumps. Profiles do not configure headers, encoding, arbitrary binary ISO 8583 bodies, decoder implementations, timestamps, or detection priorities. Custom field decoders must be registered by an enabled DLL plugin. Its header expects a process token before `RECORD`, day plus English month and time with fractional seconds, direction, connection and numeric sequence.

Minimal synthetic structural example (not real protocol rules):

```json
{
  "SchemaVersion": 1,
  "Id": "synthetic-term-demo-v1",
  "Name": "Synthetic Term demo",
  "SpecVersion": "demo-1",
  "Fields": [
    { "Id": "mti", "Name": "Message type", "Labels": ["Msg-type"], "Type": "digits", "Length": 4 },
    { "Id": "11", "Name": "Trace number", "Labels": ["Trace nbr"], "Type": "digits", "Length": 6 }
  ],
  "Messages": {
    "0200": { "Name": "Demo request", "Role": "request", "Replies": ["0210"], "Fields": [{ "Field": "mti", "Usage": "M" }, { "Field": "11", "Usage": "M" }] },
    "0210": { "Name": "Demo reply", "Role": "reply", "Replies": [], "Fields": [{ "Field": "mti", "Usage": "M" }] }
  },
  "TransactionTypes": {},
  "Accounts": {},
  "SourceAccountRequiredFor": [],
  "DestinationAccountRequiredFor": [],
  "CorrelationWindowSeconds": 300
}
```

Matching synthetic input:

```text
DEMO RECORD 1 FROM 17Jan 14:14:58.83 IN ON DEMO01 SEQ: 00001
 Msg-type=0200 Trace nbr=000123
```

Expected: one request, MTI `0200`, trace `000123`, no missing-field findings. Removing `Trace nbr` should produce a missing-field finding. This example does not exercise correlation.

## General log parser definitions (`parsers`)

Read `ParserDefinition.cs`, `JsonDefinedParser.cs`, and a matching file in `definitions/parsers/`.

Allowed top-level keys: `schemaVersion` (`1`), `id`, `name`, `strategy`, `encoding`, `detectionPrefix`, `detectionPriority`, `delimiter`, `pattern`, `fields`. Each field allows `name`, `start`, `length`, `transform`. Names must be unique; supply 1–256 fields. Supported encodings: strict `ascii` and `utf-8` only. `transform`: `none`, `trim`, `upper`, `lower`; source ranges refer to original text.

| Strategy | Rules |
| --- | --- |
| `delimited` | Literal `delimiter` of 1–8 characters; fields map in array order and counts must match. No CSV quoting/escaping or embedded record newlines. |
| `fixed-width` | Each field needs zero-based `start` and positive `length`, in decoded UTF-16 code units; `start + length` at most 65536. Short records are diagnosed; unused trailing text is allowed. |
| `regex` | .NET `pattern` up to 4096 characters; each field needs a named group matching its name. Match timeout 100 ms. Repeated groups expose their final capture. |

Use a prefix present in real input for automatic detection; an empty prefix requires manual selection. Avoid broad overlapping detection guesses. JSON regex backslashes must be escaped. One-based inclusive spec positions convert to `start = first - 1`, `length = last - first + 1` only when the spec's units match UTF-16. Byte widths in a multibyte encoding cannot generally be mapped this way.

## Fixed-width generator definitions (`generators`)

Read `GeneratorDefinition.cs`, `FixedWidthGenerator.cs`, and `definitions/generators/synthetic-fixed.json`.

Allowed top-level keys: `schemaVersion` (`1`), `id`, `name`, `encoding` (`ascii` or `utf-8`), `lineEnding` (`CRLF`, `LF`, `none`), `fields`. Supply 1–256 fields with unique names, in output order. Each allows `name`, `length` (1–65536 **encoded bytes**), `alignment` (`left` or `right`), `padding` (one single-byte encodable character, not CR/LF), and `required` (boolean, default true). Total record length at most 1 MiB. Fields are contiguous: represent spec gaps with explicit padding fields, not a nonexistent `start` property. No arbitrary constants, checksums, conditionals or binary encodings are provided by this schema.

The workspace input is a JSON object of string values. Provide a separate synthetic input object when helpful. For padding-only gaps use `required: false`, omit their input values, and set the specified padding character. Constant-valued fields still require input values; this schema cannot enforce a literal. Check missing required fields, overflow and exact generated bytes, including padding and line endings. Never truncate values silently to make a spec fit.
