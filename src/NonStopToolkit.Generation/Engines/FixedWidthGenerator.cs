using System.Text;
using NonStopToolkit.Core.Documents;
using NonStopToolkit.Core.Diagnostics;
using NonStopToolkit.Core.Encoding;
using NonStopToolkit.Generation.Abstractions;
using NonStopToolkit.Generation.Definitions;
namespace NonStopToolkit.Generation.Engines;

public sealed class FixedWidthGenerator : IFileGenerator
{
    private readonly GeneratorDefinition definition;
    public string GeneratorId => definition.Id;
    public string Name => definition.Name;
    public IReadOnlyList<string> InputNames => definition.Fields.Select(f => f.Name).ToArray();
    public FixedWidthGenerator(GeneratorDefinition definition)
    {
        definition.Validate();
        this.definition = definition with
        {
            Fields = definition.Fields.ToList()
        };
    }
    public async Task<GenerationResult> GenerateAsync(IReadOnlyDictionary<string, string> inputs, Stream output, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var encoding = StrictEncoding.Get(definition.Encoding);
        var diagnostics = new List<ParseDiagnostic>();
        var parts = new List<byte[]>();
        foreach (var key in inputs.Keys.Except(InputNames))
            diagnostics.Add(new("generate.unknown", $"Unknown field '{key}'."));
        foreach (var f in definition.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = inputs.GetValueOrDefault(f.Name) ?? "";
            if (f.Required && string.IsNullOrWhiteSpace(value))
            {
                diagnostics.Add(new("generate.required", $"'{f.Name}' is required."));
                continue;
            }
            if (value.Contains('\r') || value.Contains('\n'))
            {
                diagnostics.Add(new("generate.newline", $"'{f.Name}' cannot contain line breaks."));
                continue;
            }
            try
            {
                // Length is measured in encoded bytes, never UTF-16 characters.
                if (value.Length > f.Length)
                {
                    diagnostics.Add(new("generate.length", $"'{f.Name}' exceeds {f.Length} bytes."));
                    continue;
                }
                var bytes = encoding.GetBytes(value);
                if (bytes.Length > f.Length)
                {
                    diagnostics.Add(new("generate.length", $"'{f.Name}' exceeds {f.Length} bytes."));
                    continue;
                }
                var padded = Enumerable.Repeat(encoding.GetBytes(f.Padding)[0], f.Length).ToArray();
                bytes.CopyTo(padded, f.Alignment == "left" ? 0 : f.Length - bytes.Length);
                parts.Add(padded);
            }
            catch (EncoderFallbackException) { diagnostics.Add(new("generate.encoding", $"'{f.Name}' cannot be represented in {definition.Encoding}.")); }
        }
        if (diagnostics.Count != 0)
            return new(0, diagnostics);
        using var staged = new MemoryStream();
        foreach (var bytes in parts)
            staged.Write(bytes);
        staged.Write(encoding.GetBytes(definition.LineEnding switch
        {
            "CRLF" => "\r\n",
            "LF" => "\n",
            _ => ""
        }));
        staged.Position = 0;
        await staged.CopyToAsync(output, cancellationToken);
        return new(staged.Length, diagnostics);
    }
}
