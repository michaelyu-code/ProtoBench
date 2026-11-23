using Windows.Security.Credentials;

namespace ProtoBench.Desktop.Services;

/// <summary>Credentials are separate from plugin packages and JSON settings, scoped to a plugin and HTTPS origin.</summary>
public static class PluginUpdateCredentials
{
    private const string ResourcePrefix = "ProtoBench.PluginUpdates/";
    private static string Resource(string pluginId) => ResourcePrefix + pluginId;
    private static string Origin(Uri source) => source.GetLeftPart(UriPartial.Authority);

    private static PasswordCredential? Find(string pluginId, Uri source)
    {
        try { return new PasswordVault().Retrieve(Resource(pluginId), Origin(source)); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490)) { return null; }
    }

    public static string? Read(string pluginId, Uri source)
    {
        var credential = Find(pluginId, source);
        if (credential is null) return null;
        credential.RetrievePassword();
        return credential.Password;
    }

    public static void Save(string pluginId, Uri source, string token)
    {
        token = token.Trim();
        if (token.Length is 0 or > 4096 || token.Any(c => c < 33 || c > 126))
            throw new ArgumentException("Enter a valid GitLab access token.");
        new PasswordVault().Add(new PasswordCredential(Resource(pluginId), Origin(source), token));
    }

    public static void Remove(string pluginId, Uri source)
    {
        if (Find(pluginId, source) is { } credential) new PasswordVault().Remove(credential);
    }

    public static void RemoveAll()
    {
        var vault = new PasswordVault();
        IReadOnlyList<PasswordCredential> credentials;
        try { credentials = vault.RetrieveAll(); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070490)) { return; }
        foreach (var credential in credentials.Where(c => c.Resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
            vault.Remove(credential);
    }
}
