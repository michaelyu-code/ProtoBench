using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ProtoBench.PluginSdk;
using ProtoBench.PluginSdk.WinUI;

namespace ExampleWinUIPlugin;

public sealed class Plugin : IToolkitPlugin, ICustomToolViewProvider
{
    public IEnumerable<PluginTool> Tools =>
    [
        new("example.winui-counter", "Custom UI counter", "A minimal programmatic WinUI plugin.", [], () => new CounterSession())
        { Category = PluginToolCategory.Utilities },
        new("example.web-counter", "Web counter", "A custom HTML screen using the per-tab session.", [], () => new CounterSession())
        {
            Category = PluginToolCategory.Calculators,
            WebView = new PluginWebView("""
                <!doctype html><html lang="en"><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>:root {color-scheme: light dark} body {font:16px system-ui; padding:24px} button {font:inherit; padding:8px 16px} pre {white-space:pre-wrap}</style>
                <h1>Web counter</h1><p>This HTML screen calls a separate C# session for each tab.</p>
                <button id="run">Increment</button><pre id="output" role="status">Clicks: 0</pre>
                <script>
                const run = document.getElementById('run'), output = document.getElementById('output');
                let nextId = 0;
                window.chrome.webview.addEventListener('message', ({data}) => {
                    run.disabled = false;
                    output.textContent = data.success ? data.result.text : (data.error || data.status);
                });
                run.onclick = () => {
                    run.disabled = true;
                    window.chrome.webview.postMessage({type:'execute', id:String(++nextId), inputs:{}});
                };
                </script></html>
                """)
        }
    ];

    public FrameworkElement? CreateView(string toolId, IPluginToolSession session)
    {
        if (toolId != "example.winui-counter") return null;
        var counter = (CounterSession)session;
        var result = new TextBlock { Text = "Clicks: 0", TextWrapping = TextWrapping.Wrap };
        var button = new Button { Content = "Increment" };
        button.Click += async (_, _) =>
        {
            try { result.Text = (await counter.ExecuteAsync(new Dictionary<string, string>(), CancellationToken.None)).Text; }
            catch (Exception ex) { result.Text = ex.Message; }
        };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Custom UI counter", FontSize = 28 });
        panel.Children.Add(result);
        panel.Children.Add(button);
        return panel;
    }
}

public sealed class CounterSession : IPluginToolSession
{
    private int count;
    private bool disposed;
    public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        return Task.FromResult(new PluginToolResult($"Clicks: {++count}"));
    }
    public void Dispose() => disposed = true;
}
