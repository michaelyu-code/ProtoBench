using ProtoBench.Core.Documents;

namespace ProtoBench.Parsing.Engines;

public sealed partial class RecordMessageParser
{
    /// <summary>Explain the built-in rules on demand, without retaining an audit for every indexed record
    /// or invoking plugin decoders again. Plugin-specific outcomes remain in the supplied findings.</summary>
    public IReadOnlyList<FieldCheck> GetCheckDetails(ParsedRecord record)
    {
        var checks = new List<FieldCheck>();
        var values = record.Fields.GroupBy(f => f.DataElement ?? f.Name)
            .ToDictionary(g => g.Key, g => g.ToArray());
        string Value(string id) => values.TryGetValue(id, out var fields) ? fields[0].Value : "";
        var mti = Value("mti");
        profile.Messages.TryGetValue(mti, out var message);
        void Add(string field, string check, string outcome, string detail, string rule) =>
            checks.Add(new(field, check, outcome, detail, rule));
        Add("mti", "Message rules", message is null ? "Not evaluated" : "Passed",
            message is null ? "No message rules are configured for this MTI." : $"Using {message.Name} rules from {profile.Name}.", "message.known");
        foreach (var (id, fields) in values)
        {
            var definition = profile.Fields.FirstOrDefault(f => f.Id == id);
            Add(id, "Profile definition", definition is null ? "Not evaluated" : "Passed",
                definition is null ? "The field is not defined in this profile." : $"Recognized as {definition.Name}.", "field.known");
            Add(id, "Duplicate detection", fields.Length == 1 ? "Passed" : "Review",
                $"{fields.Length} occurrence(s) found; all occurrences are preserved.", "field.duplicate");
            if (definition is null) continue;
            var rule = message?.Fields.FirstOrDefault(r => r.Field == id);
            foreach (var field in fields)
            {
                var length = rule?.Length ?? definition.Length;
                if (length is int expected)
                    Add(id, "Exact length", field.Value.Length == expected ? "Passed" : "Failed",
                        $"Expected {expected} characters; found {field.Value.Length}.", "field.format.length");
                if (definition.MaxLength is int maximum)
                    Add(id, "Maximum length", field.Value.Length <= maximum ? "Passed" : "Failed",
                        $"Maximum {maximum} characters; found {field.Value.Length}.", "field.format.max-length");
                if (definition.Type == "digits")
                    Add(id, "Character type", field.Value.All(char.IsAsciiDigit) ? "Passed" : "Failed",
                        "Checked that every character is an ASCII digit (0–9).", "field.format.type");
                else if (definition.Type == "hex")
                {
                    Add(id, "Character type", field.Value.All(Uri.IsHexDigit) ? "Passed" : "Failed",
                        "Checked that every character is hexadecimal (0–9, A–F).", "field.format.type");
                    Add(id, "Hexadecimal byte alignment", field.Value.Length % 2 == 0 ? "Passed" : "Failed",
                        $"Expected an even number of hexadecimal characters; found {field.Value.Length}.", "field.format.hex-pairs");
                }
                if (length is null && definition.MaxLength is null && definition.Type == "text")
                    Add(id, "Format constraints", "Not configured", "This profile sets no character-type or length restriction.", "field.format");
            }
            if (definition.Decoder is { } decoder)
            {
                var failed = record.Findings.FirstOrDefault(f => f.Field == id && f.RuleId is "decode.failed" or "decode.unavailable");
                Add(id, "Field decoder", failed is null ? "Completed" : "Not completed",
                    failed?.Explanation ?? $"Decoder '{decoder}' ran. See its reported findings below; individual successful plugin checks are not reported.", "field.decoder");
            }
        }
        foreach (var rule in message?.Fields ?? [])
        {
            var usage = rule.Usage;
            var ruleId = $"{profile.Id}:{mti}:{rule.Field}";
            if (usage == "C")
            {
                if (rule.WhenField is null || Value(rule.WhenField).Length == 0)
                {
                    Add(rule.Field, "Conditional presence", "Not evaluated", "The condition requires additional field or transaction context. " + rule.Comment, ruleId);
                    continue;
                }
                var matches = rule.WhenValues!.Any(v => rule.WhenPrefix ? Value(rule.WhenField).StartsWith(v, StringComparison.Ordinal) : Value(rule.WhenField) == v);
                Add(rule.Field, "Conditional presence", matches && !rule.RequireWhenMatched ? "Review" : "Evaluated",
                    $"Condition on field {rule.WhenField} {(matches ? "matched" : "did not match")}. " + rule.Comment, ruleId);
                usage = matches ? rule.RequireWhenMatched ? "M" : "O" : rule.ForbidOtherwise ? "F" : "O";
            }
            var present = values.ContainsKey(rule.Field);
            var valid = usage switch { "M" => present && Value(rule.Field).Length > 0, "F" => !present, _ => true };
            Add(rule.Field, "Field presence", valid ? "Passed" : "Failed",
                $"{(usage == "M" ? "Mandatory" : usage == "F" ? "Forbidden" : "Optional")} field; {(present ? Value(rule.Field).Length > 0 ? "present with a value" : "present but empty" : "absent")}. " + rule.Comment, ruleId);
        }
        return checks;
    }
}
