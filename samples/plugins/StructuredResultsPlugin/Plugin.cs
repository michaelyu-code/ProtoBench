using System.Globalization;
using ProtoBench.PluginSdk;

namespace StructuredResultsPlugin;

/// <summary>Synthetic examples of the standard host UI; these do not implement a real spec search engine.</summary>
public sealed class Plugin : IToolkitPlugin
{
    public IEnumerable<PluginTool> Tools =>
    [
        new("example.spec-search", "Example spec search", "Search a small synthetic specification.",
            [new("spec", "Specification", ToolInputKind.Choice, "Synthetic v1", Choices: ["Synthetic v1"]),
             new("query", "Search terms", DefaultValue: "response code", Required: true),
             new("max", "Max results", ToolInputKind.Integer, "10", Required: true) { Minimum = 1, Maximum = 100 }],
            () => new SearchSession(), ActionLabel: "Search", Help: "Synthetic demonstration. Matches retain example order; no BM25 ranking is calculated."),
        new("example.spec-field", "Example spec field decoder", "Decode a response code from a synthetic specification.",
            [new("spec", "Specification", ToolInputKind.Choice, "Synthetic v1", Choices: ["Synthetic v1"]),
             new("field", "Field", ToolInputKind.Choice, "DE39", Choices: ["DE39"]),
             new("mti", "Message type", DefaultValue: "0210", Required: true, Description: "Four numeric digits; example supports 0210 response messages."),
             new("value", "Field value", DefaultValue: "05", Required: true, Description: "Two numeric digits, including leading zeroes."),
             new("other", "Other fields", ToolInputKind.FieldValues, "{}") { IsAdvanced = true }],
            () => new DecodeSession(), ActionLabel: "Decode", Help: "Illustrative mappings only. Supporting fields demonstrate the editor but are not used by these synthetic rules.")
    ];
}

public sealed class SearchSession : IPluginToolSession
{
    private static readonly ToolSearchHit[] Hits =
    [
        new("DE39 · Response code", "The response code identifies the transaction outcome.",
            "The response code identifies the transaction outcome. 00 means Approved; 05 means Declined. Other values are unmapped in this example.")
        { Reference = new("Synthetic v1", "§ 1 · Response code") },
        new("Response message", "A response message carries a response code in DE39.",
            "A response message carries a response code in DE39. This synthetic specification defines response MTI 0210.")
        { Reference = new("Synthetic v1", "§ 2 · Response message") }
    ];

    public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var query = inputs["query"].Trim();
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matches = Hits.Where(hit => terms.All(term => (hit.Title + " " + hit.Passage).Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        var hits = matches.Take(int.Parse(inputs["max"], CultureInfo.InvariantCulture)).ToArray();
        return Task.FromResult(new PluginToolResult(string.Join("\n\n", hits.Select(hit => hit.Title + "\n" + hit.Passage)))
        { Search = new(query, hits) { TotalMatches = matches.Length, HighlightTerms = terms } });
    }
    public void Dispose() { }
}

public sealed class DecodeSession : IPluginToolSession
{
    public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var value = inputs["value"];
        var other = ToolInputValues.ParseFieldValues(inputs["other"]);
        var valid = value.Length == 2 && value.All(char.IsAsciiDigit);
        var meaning = valid ? value switch { "00" => "Approved (synthetic)", "05" => "Declined (synthetic)", _ => "Unmapped value" } : "Expected two numeric digits.";
        var status = !valid ? ToolDecodeStatus.InvalidInput : meaning == "Unmapped value" ? ToolDecodeStatus.Unmapped : ToolDecodeStatus.Decoded;
        if (inputs["mti"] != "0210") { meaning = "This example only defines rules for MTI 0210."; status = ToolDecodeStatus.MissingContext; }
        var result = new ToolDecodeResult("DE39 · Response code", value, meaning,
            valid ? [new("Transaction outcome", value, meaning) { Position = "Entire field" }] : [])
        {
            Status = status,
            ContextUsed = [new("Specification", inputs["spec"]), new("Message type", inputs["mti"])],
            Warnings = other.Count > 0 ? ["Supporting fields were supplied but are not used by this example rule."] : [],
            References = [new("Synthetic v1", "§ 1 · Response code", "00 = Approved; 05 = Declined. Other values are unmapped.")]
        };
        return Task.FromResult(new PluginToolResult($"DE39: {value}\n{meaning}") { Decoding = result });
    }
    public void Dispose() { }
}
