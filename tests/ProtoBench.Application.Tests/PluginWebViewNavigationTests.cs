using System.Text;
using ProtoBench.Desktop.Features.Plugins;

namespace ProtoBench.Application.Tests;

public sealed class PluginWebViewNavigationTests
{
    [Theory]
    [InlineData("<h1>Sample</h1>")]
    [InlineData("<h1>Custom screen — 日本語 😀</h1>")]
    public void HtmlStringNavigationAcceptsItsEncodedDocumentAndBlankSource(string html)
    {
        var navigation = new PluginWebViewNavigation(html);
        var uri = "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
        Assert.False(navigation.Cancel(uri, 1));
        Assert.False(navigation.Cancel("about:blank", 2));
        Assert.False(navigation.WasBlocked(1));
        Assert.False(navigation.WasBlocked(2));
    }

    [Theory]
    [InlineData("https://example.invalid/")]
    [InlineData("file:///C:/private.html")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<h1>Other</h1>")]
    [InlineData("data:text/html;charset=utf-8;base64,PGgxPk90aGVyPC9oMT4=")]
    [InlineData("about:blank.evil")]
    public void OtherPagesAreBlockedAndRecognizedWithoutMaskingActualLoadFailures(string uri)
    {
        var navigation = new PluginWebViewNavigation("<h1>Sample</h1>");
        Assert.True(navigation.Cancel(uri, 3));
        Assert.False(navigation.WasBlocked(4));
        Assert.True(navigation.WasBlocked(3));
        Assert.False(navigation.WasBlocked(3));
    }
}
