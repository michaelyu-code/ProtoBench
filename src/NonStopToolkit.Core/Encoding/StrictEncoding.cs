namespace NonStopToolkit.Core.Encoding;

public static class StrictEncoding
{
    public static System.Text.Encoding Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An explicit encoding is required.");
        // Explicit allowlist: no implicit EBCDIC page, BOM, byte order, or replacement fallback.
        return name.ToLowerInvariant() switch
        {
            "utf-8" => new System.Text.UTF8Encoding(false, true),
            "ascii" => System.Text.Encoding.GetEncoding("us-ascii", System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback),
            _ => throw new ArgumentException($"Unsupported encoding '{name}'. Supported: utf-8, ascii.")
        };
    }
}
