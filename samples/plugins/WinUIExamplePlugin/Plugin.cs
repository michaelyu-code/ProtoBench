using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NonStopToolkit.PluginSdk;
using NonStopToolkit.PluginSdk.WinUI;

namespace ExampleWinUIPlugin;

public sealed class Plugin : IToolkitPlugin, ICustomToolViewProvider
{
    public IEnumerable<PluginTool> Tools =>
    [
        new("example.winui-counter", "Custom UI counter", "A minimal programmatic WinUI plugin.", [], () => new CounterSession())
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
