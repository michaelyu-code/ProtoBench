using ProtoBench.Parsing.Definitions;

namespace ProtoBench.Infrastructure.Configuration;

public static class Iso8583Profiles
{
    public static DefinitionLoadResult<Iso8583Profile> Load(IEnumerable<string> directories)
    {
        // Use filenames as IDs; unchanged jPOS XML needs no host-specific attributes.
        var profiles = new List<Iso8583Profile>();
        var diagnostics = new List<string>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in directories.Where(Directory.Exists))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.xml").Order(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        if (new FileInfo(file).Length > 1048576) throw new InvalidDataException("Definition exceeds 1 MiB.");
                        var id = Path.GetFileNameWithoutExtension(file);
                        var profile = Iso8583Profile.FromXml(File.ReadAllText(file), id);
                        if (ids.Add(id)) profiles.Add(profile);
                        else diagnostics.Add($"{Path.GetFileName(file)}: duplicate ISO 8583 profile ID '{id}'.");
                    }
                    catch (Exception ex) when (ex is ArgumentException or FormatException or System.Xml.XmlException or IOException or UnauthorizedAccessException)
                    { diagnostics.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { diagnostics.Add($"Cannot read ISO 8583 profiles: {ex.Message}"); }
        }
        return new(profiles, diagnostics);
    }
}
