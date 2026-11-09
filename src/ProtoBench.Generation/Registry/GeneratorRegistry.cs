using ProtoBench.Generation.Abstractions;
namespace ProtoBench.Generation.Registry;

public interface IGeneratorRegistry
{
    IReadOnlyList<IFileGenerator> Generators
    {
        get;
    }
    IFileGenerator Get(string id);
}
public sealed class GeneratorRegistry : IGeneratorRegistry
{
    public IReadOnlyList<IFileGenerator> Generators
    {
        get;
    }
    public GeneratorRegistry(IEnumerable<IFileGenerator> generators)
    {
        var list = generators.ToArray();
        if (list.Any(g => string.IsNullOrWhiteSpace(g.GeneratorId)) || list.Select(g => g.GeneratorId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Length)
            throw new ArgumentException("Empty or duplicate generator ID.");
        Generators = Array.AsReadOnly(list);
    }
    public IFileGenerator Get(string id) => Generators.FirstOrDefault(g => g.GeneratorId.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"Unknown generator '{id}'.");
}
