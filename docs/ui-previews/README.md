# Specification tool UI previews

These interactive design references describe the proposed plugin UIs. They use illustrative specifications and mappings, not production search or decoding logic. They do not change the desktop app.

- [Spec search](spec-search.html): ranked results with highlighted excerpts, a selected passage preview, specification locations, and collapsed raw output and ranking details.
- [Spec field decoder](spec-field-decoder.html): inputs beside a decoded summary and component/value/meaning table, supporting field rows, context used, specification references, and collapsed raw output. Editing inputs invalidates the displayed result.

The files are HTML fragments for the conversation preview. Their main interactions also work when opened directly in a browser; optional host icons, appearance, and design controls may differ. The search preview filters a fixed example dataset rather than running BM25. The decoder supports a small set of example DE22 and DE39 mappings; supporting fields are editable placeholders and do not affect its example rules. Specification references and page numbers are illustrative.

For implementation, a plugin can use `ICustomToolViewProvider` from `ProtoBench.PluginSdk.WinUI` to replace the default text result and field tree. Keep search and decoding logic in the session, and return structured data to populate the custom view. See [plugin documentation](../PLUGINS.md) and `samples/plugins/WinUIExamplePlugin/Plugin.cs`.

Preserve unknown values and distinguish invalid input, partial decoding, and missing context. Show provenance only when supplied by the real specification. For narrow windows, stack the results beneath the inputs or selected passage beneath the result list.
