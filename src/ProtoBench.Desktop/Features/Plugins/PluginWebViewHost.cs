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
    private int pendingEvents;

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
        workspace.WebMessageReceived += ReceiveEvent;
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
            var navigation = new PluginWebViewNavigation(definition.Html);
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.NavigationStarting += (_, e) => e.Cancel = navigation.Cancel(e.Uri, e.NavigationId);
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += Receive;
            core.NavigationCompleted += (_, e) =>
            {
                if (navigation.WasBlocked(e.NavigationId)) return;
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

    private void ReceiveEvent(object? sender, string message)
    {
        // Sessions may publish from TCP workers. WebView2 must only be touched on its UI thread.
        if (Interlocked.Increment(ref pendingEvents) > 256)
        {
            Interlocked.Decrement(ref pendingEvents);
            return;
        }
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (!closed && web.CoreWebView2 is { } core) core.PostWebMessageAsJson(message);
            }
            catch (Exception ex) { if (!closed) ShowError($"Custom screen event failed: {ex.Message}"); }
            finally { Interlocked.Decrement(ref pendingEvents); }
        })) Interlocked.Decrement(ref pendingEvents);
    }

    private void Close(object? sender, EventArgs args)
    {
        closed = true;
        workspace.Disposed -= Close;
        workspace.WebMessageReceived -= ReceiveEvent;
        Loaded -= Initialize;
        web.Close();
    }
}
