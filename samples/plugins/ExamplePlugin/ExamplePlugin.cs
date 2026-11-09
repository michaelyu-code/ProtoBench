using System.Security.Cryptography;
using System.Text;
using ProtoBench.Core.Documents;
using ProtoBench.Core.Fields;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.PluginSdk;

namespace ExamplePlugin;

public sealed class ExamplePlugin : IToolkitPlugin
{
    public IEnumerable<PluginTool> Tools =>
    [
        new("example.sha256", "SHA-256 text", "Calculate a SHA-256 digest of UTF-8 text.",
            [new("text", "Text", ToolInputKind.Multiline, Description: "Whitespace and line breaks are included exactly as entered.")],
            () => new HashSession(), ActionLabel: "Calculate", Help: "Sample developer plugin. Input is processed locally and is not saved.")
    ];
    public IEnumerable<ILogParser> Parsers => [new ExampleLogParser()];
    public IEnumerable<ProtoBench.Generation.Abstractions.IFileGenerator> Generators =>
    [
        new ProtoBench.Generation.Engines.FixedWidthGenerator(new()
        {
            Id = "example.fixed", Name = "Example eight-byte record", Encoding = "ascii", LineEnding = "CRLF",
            Fields = [new() { Name = "value", Length = 8 }]
        })
    ];
    public IEnumerable<IFieldDecoder> FieldDecoders => [new ResponseDecoder()];
}

public sealed class HashSession : IPluginToolSession
{
    public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new PluginToolResult(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputs["text"])))));
    }
    public void Dispose() { }
}

public sealed class ResponseDecoder : IFieldDecoder
{
    public string Id => "example.response";
    public FieldDecodeResult Decode(FieldDecodeContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var meaning = context.Field.Value switch { "00" => "Approved (synthetic)", "05" => "Declined (synthetic)", _ => "Unmapped" };
        return new([new("Meaning", meaning, context.Field.Source) { Description = "Synthetic example response-code mapping." }])
        {
            Specification = "Synthetic response-code specification\n\nCode  Meaning\n────  ────────────────────\n00    Approved (synthetic)\n05    Declined (synthetic)\n\nOther values are unmapped."
        };
    }
}

public sealed class ExampleLogParser : IStreamingLogParser
{
    private readonly ProtoBench.Parsing.Engines.JsonDefinedParser engine = new(new()
    {
        Id = "example.lines", Name = "Example EX: log", Strategy = "regex", Encoding = "ascii",
        DetectionPrefix = "EX:", DetectionPriority = 25, Pattern = "^EX:(?<message>.*)$",
        Fields = [new() { Name = "message" }]
    });
    public string FormatId => "example.lines";
    public string Name => "Example EX: log";
    public int DetectionPriority => 25;
    public bool CanParse(ReadOnlySpan<byte> sample) => sample.StartsWith("EX:"u8);
    public Task<ParseResult> ParseAsync(Stream input, CancellationToken token = default) => engine.ParseAsync(input, token);
    public Task ParseToAsync(Stream input, ILogParseSink sink, CancellationToken token = default) => engine.ParseToAsync(input, sink, token);
}
