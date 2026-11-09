using NonStopToolkit.Core.Documents;
namespace NonStopToolkit.Generation.Abstractions;

public interface IFileGenerator
{
    string GeneratorId
    {
        get;
    }
    string Name
    {
        get;
    }
    IReadOnlyList<string> InputNames
    {
        get;
    }
    Task<GenerationResult> GenerateAsync(IReadOnlyDictionary<string, string> inputs, Stream output, CancellationToken cancellationToken = default);
}
