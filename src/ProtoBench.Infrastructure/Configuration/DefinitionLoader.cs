namespace ProtoBench.Infrastructure.Configuration;

public sealed record DefinitionLoadResult<T>(IReadOnlyList<T> Definitions, IReadOnlyList<string> Diagnostics);
public static class DefinitionLoader
{
    public static DefinitionLoadResult<T> Load<T>(IEnumerable<string> directories, Func<string, T> parse, Func<T, string> getId)
    {
        var definitions = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<string>();
        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
                continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 1048576)
                            throw new InvalidDataException("Definition exceeds 1 MiB.");
                        var item = parse(File.ReadAllText(file));
                        if (!definitions.TryAdd(getId(item), item))
                            diagnostics.Add($"{Path.GetFileName(file)}: duplicate definition ID '{getId(item)}'; use a unique ID for custom definitions.");
                    }
                    catch (Exception ex) when (ex is ArgumentException or System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
                    {
                        diagnostics.Add($"{Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { diagnostics.Add($"Cannot read definitions directory: {ex.Message}"); }
        }
        return new(definitions.Values.ToArray(), diagnostics);
    }
}
