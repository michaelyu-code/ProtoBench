using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ProtoBench.Application.Workspaces;
using ProtoBench.PluginSdk;

namespace ProtoBench.Desktop.Features.Plugins;

/// <summary>One browser per workspace. Tab switches preserve it; workspace disposal closes it.</summary>
internal sealed class PluginWebViewHost : Grid
{
    private readonly WebView2 web = new();
    private readonly TextBlock error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), Visibility = Visibility.Collapsed };
    private readonly PluginWebViewBridge bridge;
    private readonly PluginToolWorkspace workspace;
    private readonly PluginWebView definition;
    private bool initializing, closed;

    public PluginWebViewHost(PluginToolWorkspace workspace, PluginWebView definition)
    {
        this.workspace = workspace;
        this.definition = definition;
        bridge = new(workspace);
        Children.Add(web);
        Children.Add(error);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(web, workspace.Title + " custom tool");
        Loaded += Initialize;
        workspace.Disposed += Close;
    }

    private async void Initialize(object sender, RoutedEventArgs args)
    {
        if (initializing || closed) return;
        initializing = true;
        try
        {
            // Tool IDs are identifiers, not paths. Hash before using them as a profile directory.
            var profile = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(workspace.ToolId)));
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProtoBench", "PluginWebViews", profile);
            var environment = await CoreWebView2Environment.CreateWithOptionsAsync(null, data, null);
            if (closed) return;
            await web.EnsureCoreWebView2Async(environment);
            if (closed) return;
            var core = web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.NavigationStarting += (_, e) => { if (e.Uri != "about:blank") e.Cancel = true; };
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += Receive;
            core.NavigationCompleted += (_, e) =>
            {
                if (!closed && !e.IsSuccess) ShowError($"Could not load the custom screen: {e.WebErrorStatus}");
            };
            web.NavigateToString(definition.Html);
        }
        catch (Exception ex) { if (!closed) ShowError($"Could not start the custom screen. Check that Microsoft Edge WebView2 Runtime is installed. {ex.Message}"); }
    }

    private async void Receive(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (closed || args.Source != "about:blank") return;
        try
        {
            var response = await bridge.ReceiveAsync(args.WebMessageAsJson);
            if (!closed) sender.PostWebMessageAsJson(response);
        }
        catch (Exception ex) { if (!closed) ShowError($"Custom screen message failed: {ex.Message}"); }
    }

    private void ShowError(string message) { error.Text = message; error.Visibility = Visibility.Visible; }

    private void Close(object? sender, EventArgs args)
    {
        closed = true;
        workspace.Disposed -= Close;
        Loaded -= Initialize;
        web.Close();
    }
}
