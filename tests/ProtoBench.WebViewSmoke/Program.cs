using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.Application.Workspaces;
using ProtoBench.Desktop.Features.Plugins;

namespace ProtoBench.WebViewSmoke;

internal static class Program
{
    internal static string ReportPath = "";
    [STAThread]
    private static void Main(string[] args)
    {
        ReportPath = Path.GetFullPath(args.Single());
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", ReportPath + ".profile");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new SmokeApp();
        });
    }
}

public sealed partial class SmokeApp : Microsoft.UI.Xaml.Application
{
    private readonly List<string> observations = [];
    private Window? window;
    public SmokeApp() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var sample = new ExampleWinUIPlugin.Plugin().Tools.Single(t => t.WebView is not null);
            using var workspace = new PluginToolWorkspace(sample with { Id = "test.webview-smoke." + Guid.NewGuid().ToString("N") });
            var host = new PluginWebViewHost(workspace, sample.WebView!);
            var web = host.Children.OfType<WebView2>().Single();
            web.CoreWebView2Initialized += (_, _) =>
            {
                web.CoreWebView2.NavigationStarting += (_, e) => observations.Add($"NavigationStarting: {e.Uri[..Math.Min(160, e.Uri.Length)]}");
                web.CoreWebView2.NavigationCompleted += (_, e) => observations.Add($"NavigationCompleted: success={e.IsSuccess}; status={e.WebErrorStatus}; source={web.CoreWebView2.Source}");
            };
            window = new Window { Title = "ProtoBench WebView smoke test", Content = host };
            window.Activate();
            await WaitUntil(async () => web.CoreWebView2 is not null &&
                await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#run') !== null") == "true", "sample screen load", host);
            observations.Add("Sample HTML rendered.");
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#run').click()");
            await WaitUntil(async () => await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#output').textContent") == "\"Clicks: 1\"", "session response", host);
            observations.Add("Sample button received the C# session result.");
            using (var screenshot = JsonDocument.Parse(await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\"}")))
                File.WriteAllBytes(Program.ReportPath + ".png", Convert.FromBase64String(screenshot.RootElement.GetProperty("data").GetString()!));
            foreach (var destination in new[] { "https://example.invalid/blocked", "data:text/html,<h1>Other document</h1>" })
            {
                var completed = new TaskCompletionSource();
                void OnCompleted(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) => completed.TrySetResult();
                web.CoreWebView2.NavigationCompleted += OnCompleted;
                try
                {
                    web.CoreWebView2.Navigate(destination);
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await WaitUntil(async () => await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#output').textContent") == "\"Clicks: 1\"", "blocked navigation preserved screen", host);
                }
                finally { web.CoreWebView2.NavigationCompleted -= OnCompleted; }
            }
            observations.Add("External and unrelated data-URL navigation stayed blocked without a load-error banner.");
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#run').click()");
            await WaitUntil(async () => await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#output').textContent") == "\"Clicks: 2\"", "session after blocked navigation", host);
            observations.Add("Session bridge still worked after blocked navigation.");
            // Switching content follows the same Loaded/Unloaded lifecycle as switching workspace tabs.
            var secondDefinition = sample with { WebView = new(sample.WebView!.Html.Replace("Web counter", "Web counter — 日本語 😀")) };
            using var secondWorkspace = new PluginToolWorkspace(secondDefinition);
            var secondHost = new PluginWebViewHost(secondWorkspace, secondDefinition.WebView!);
            var secondWeb = secondHost.Children.OfType<WebView2>().Single();
            window.Content = secondHost;
            await WaitUntil(async () => secondWeb.CoreWebView2 is not null &&
                await secondWeb.CoreWebView2.ExecuteScriptAsync("document.querySelector('#run') !== null") == "true", "second screen with Unicode HTML", secondHost);
            await secondWeb.CoreWebView2.ExecuteScriptAsync("document.querySelector('#run').click()");
            await WaitUntil(async () => await secondWeb.CoreWebView2.ExecuteScriptAsync("document.querySelector('#output').textContent") == "\"Clicks: 1\"", "independent second session", secondHost);
            window.Content = host;
            await WaitUntil(async () => await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#output').textContent") == "\"Clicks: 2\"", "restored first screen", host);
            await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#run').click()");
            await WaitUntil(async () => await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('#output').textContent") == "\"Clicks: 3\"", "restored first session", host);
            if (secondWorkspace.Output != "Clicks: 1") throw new InvalidOperationException("Second session changed when the first screen ran.");
            observations.Add("Unicode HTML loaded; switching screens preserved separate sessions and browser state.");
            Save(true, null);
        }
        catch (Exception ex) { Save(false, ex.ToString()); }
        finally { window?.Close(); Exit(); }
    }

    private static async Task WaitUntil(Func<Task<bool>> condition, string step, PluginWebViewHost host)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            var error = host.Children.OfType<TextBlock>().FirstOrDefault(t => t.Visibility == Visibility.Visible);
            if (error is not null) throw new InvalidOperationException($"{step}: {error.Text}");
            if (await condition()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException(step);
    }

    private void Save(bool success, string? error)
    {
        File.WriteAllText(Program.ReportPath, JsonSerializer.Serialize(new { success, error, observations }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = success ? 0 : 1;
    }
}
