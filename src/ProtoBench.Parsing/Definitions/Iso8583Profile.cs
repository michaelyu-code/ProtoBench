using System.Xml;
using System.Xml.Linq;

namespace ProtoBench.Parsing.Definitions;

public sealed record Iso8583FieldDefinition(int Id, int Length, string Name, string Class, bool Pad)
{
    public bool Trim { get; init; }
}

/// <summary>A declarative subset of jPOS GenericPackager; Java classes are never executed.</summary>
public sealed record Iso8583Profile(string Id, string Name, IReadOnlyDictionary<int, Iso8583FieldDefinition> Fields, int HeaderLength)
{
    public int MaxValidField { get; init; } = 128;
    public static Iso8583Profile FromXml(string xml, string id, string? name = null)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 1048576
        });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != "isopackager") throw new ArgumentException("Expected a jPOS isopackager root.");
        foreach (var attribute in root.Attributes())
            if (attribute.Name.LocalName is not ("headerLength" or "maxValidField" or "emitBitmap" or "bitmapField" or "firstField"))
                throw new ArgumentException($"Unsupported packager attribute '{attribute.Name}'. Use a standard primary/secondary bitmap profile.");
        if (((bool?)root.Attribute("emitBitmap") ?? true) != true ||
            ((int?)root.Attribute("bitmapField") ?? 1) != 1 || ((int?)root.Attribute("firstField") ?? 2) != 2)
            throw new ArgumentException("This reader requires emitBitmap=true, bitmapField=1 and firstField=2.");
        var maxField = (int?)root.Attribute("maxValidField") ?? 128;
        if (maxField is < 2 or > 192) throw new ArgumentException("maxValidField must be 2–192.");
        var header = (int?)root.Attribute("headerLength") ?? 0;
        if (header is < 0 or > 65536) throw new ArgumentException("Invalid headerLength.");
        var fields = new Dictionary<int, Iso8583FieldDefinition>();
        foreach (var element in root.Elements())
        {
            if (element.Name != "isofield" && element.Name != "isofieldpackager")
                throw new ArgumentException($"Unsupported element '{element.Name}'.");
            var number = (int?)element.Attribute("id") ?? -1;
            var length = (int?)element.Attribute("length") ?? -1;
            if (number is < 0 or > 192 || length is < 1 or > 65536)
                throw new ArgumentException("Field IDs must be 0–192 and lengths 1–65536.");
            var type = (string?)element.Attribute("class") ?? "";
            // Composite fields retain an unsupported type so earlier fields can still be parsed.
            if (element.Name == "isofieldpackager" || element.Attributes().Any(a => a.Name.LocalName is not ("id" or "length" or "name" or "class" or "pad" or "trim")))
                type = "unsupported:" + type;
            var pad = (bool?)element.Attribute("pad") ?? false;
            var trim = (bool?)element.Attribute("trim") ?? false;
            if (!fields.TryAdd(number, new(number, length, (string?)element.Attribute("name") ?? $"Field {number}", type, pad) { Trim = trim }))
                throw new ArgumentException($"Duplicate field {number}.");
        }
        if (!fields.ContainsKey(0) || !fields.TryGetValue(1, out var bitmap) ||
            bitmap.Class is not ("org.jpos.iso.IFA_BITMAP" or "org.jpos.iso.IFB_BITMAP" or "org.jpos.iso.IFE_BITMAP") || bitmap.Length is not (8 or 16 or 24))
            throw new ArgumentException("Define field 0 (MTI) and field 1 using IFA_BITMAP, IFB_BITMAP or IFE_BITMAP, length 8, 16 or 24.");
        return new(id, name ?? id, fields, header) { MaxValidField = maxField };
    }
}
