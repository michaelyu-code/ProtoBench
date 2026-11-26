using System.Text;

namespace ProtoBench.Desktop.Features.Plugins;

/// <summary>NavigateToString reports an encoded data URL during navigation, then about:blank as its source.</summary>
internal sealed class PluginWebViewNavigation(string html)
{
    private readonly string documentUri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
    private readonly HashSet<ulong> blocked = [];

    public bool Cancel(string uri, ulong navigationId)
    {
        if (uri == "about:blank" || uri == documentUri) return false;
        blocked.Add(navigationId);
        return true;
    }

    // A deliberately blocked link leaves the current screen intact; it is not a screen-load failure.
    public bool WasBlocked(ulong navigationId) => blocked.Remove(navigationId);
}
