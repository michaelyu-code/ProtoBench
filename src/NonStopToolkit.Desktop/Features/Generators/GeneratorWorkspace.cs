using NonStopToolkit.Application.Workspaces;
using NonStopToolkit.Core.Documents;
using NonStopToolkit.Generation.Registry;
using NonStopToolkit.Generation.Abstractions;
using NonStopToolkit.Desktop.Services;
using System.Text.Json;
using Microsoft.Extensions.Logging;
namespace NonStopToolkit.Desktop.Features.Generators;

public sealed class GeneratorWorkspace(IGeneratorRegistry registry, IAtomicFileWriter writer, SessionSettings settings, ILogger<GeneratorWorkspace> logger) : ToolWorkspace
{
    public override string ToolId => "file-generator";
    public override string Title => "File Generator";
    public IReadOnlyList<IFileGenerator> Generators => registry.Generators;
    private string? generatorId;
    public string? GeneratorId
    {
        get => generatorId; set
        {
            SetProperty(ref generatorId, value);
            Preview = [];
        }
    }
    private string inputs = "{\n  \"kind\": \"FX\",\n  \"name\": \"ALPHA\",\n  \"count\": \"42\"\n}";
    public string Inputs
    {
        get => inputs; set
        {
            SetProperty(ref inputs, value);
            Preview = [];
        }
    }
    private byte[] preview = [];
    public byte[] Preview
    {
        get => preview; private set => SetProperty(ref preview, value);
    }
    private string diagnostics = "";
    public string Diagnostics
    {
        get => diagnostics; private set => SetProperty(ref diagnostics, value);
    }
    private async Task<byte[]?> GenerateAsync(CancellationToken token)
    {
        var id = GeneratorId ?? throw new InvalidOperationException("Select a generator.");
        var text = Inputs;
        var generated = await Task.Run(async () =>
        {
            if (text.Length > 1048576)
                throw new FormatException("Inputs exceed 1 MiB.");
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(text) ?? throw new FormatException("Enter a JSON object of string values.");
            using var output = new MemoryStream();
            var result = await registry.Get(id).GenerateAsync(values, output, token);
            return (Result: result, Bytes: output.ToArray());
        }, token);
        Diagnostics = string.Join(Environment.NewLine, generated.Result.Diagnostics.Select(d => d.Message));
        return generated.Result.Success ? generated.Bytes : null;
    }
    public Task PreviewAsync() => RunAsync(async token => { Preview = []; Preview = await GenerateAsync(token) ?? []; });
    public Task SaveAsync(string path) => RunAsync(async token =>
    {
        try
        {
            var bytes = await GenerateAsync(token);
            if (bytes is null)
            {
                Preview = [];
                return;
            }
            await writer.WriteAsync(path, async (stream, ct) => await stream.WriteAsync(bytes, ct), token);
            Preview = bytes;
            await settings.AddRecentAsync(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(new EventId(301), ex, "Generator workspace failed"); throw; }
    });
}
