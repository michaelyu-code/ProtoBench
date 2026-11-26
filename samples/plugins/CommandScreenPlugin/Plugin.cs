using System.Text.Json;
using ProtoBench.PluginSdk;

namespace CommandScreenPlugin;

public sealed class Plugin : IToolkitPlugin
{
    public IEnumerable<PluginTool> Tools =>
    [
        new("example.command-screen", "Concurrent screen", "Independent requests, cancellation and live events.", [], () => new CommandSession())
        {
            WebView = new("""
                <!doctype html><html lang="en"><meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <style>:root{color-scheme:light dark}body{font:16px system-ui;padding:24px}button{font:inherit;margin:4px;padding:8px}pre{white-space:pre-wrap}</style>
                <h1>Concurrent screen</h1><p>The short request finishes while the long request is still running.</p>
                <button id="run">Run two requests</button><button id="cancel" disabled>Cancel long request</button>
                <pre id="output" role="log" aria-live="polite"></pre>
                <script>
                const pending = new Map(); let nextId = 0, longRequest;
                const log = text => document.querySelector('#output').textContent += text + '\n';
                const post = message => window.chrome.webview.postMessage(message);
                function request(type, body) {
                    const id = String(++nextId);
                    const promise = new Promise((resolve, reject) => {
                        pending.set(id, {resolve, reject});
                        post({type, id, ...body});
                    });
                    return {id, promise};
                }
                window.chrome.webview.addEventListener('message', ({data}) => {
                    if (data.type === 'event') { log('Event: ' + data.name + ' ' + JSON.stringify(data.data)); return; }
                    const waiter = pending.get(data.id); if (!waiter) return;
                    pending.delete(data.id);
                    data.success ? waiter.resolve(data) : waiter.reject(new Error(data.error || data.status));
                });
                document.querySelector('#run').onclick = async () => {
                    document.querySelector('#run').disabled = true;
                    const cancel = document.querySelector('#cancel'); cancel.disabled = false;
                    longRequest = request('command', {command:'wait', arguments:{label:'long', milliseconds:2000}});
                    const shortRequest = request('command', {command:'wait', arguments:{label:'short', milliseconds:100}});
                    const show = async task => {
                        try { const reply = await task.promise; log('Result: ' + reply.result.label); }
                        catch (error) { log(error.message); }
                    };
                    await Promise.all([show(longRequest), show(shortRequest)]);
                    longRequest = null; cancel.disabled = true; document.querySelector('#run').disabled = false;
                };
                document.querySelector('#cancel').onclick = async () => {
                    if (!longRequest) return;
                    try { await request('cancelCommand', {targetId:longRequest.id}).promise; }
                    catch (error) { log(error.message); }
                };
                </script></html>
                """)
        }
    ];
}

public sealed class CommandSession : IPluginCommandSession
{
    public PluginCommandConcurrency CommandConcurrency => PluginCommandConcurrency.Parallel;
    public event EventHandler<PluginToolEvent>? EventReceived;
    public Task<PluginToolResult> ExecuteAsync(IReadOnlyDictionary<string, string> inputs, CancellationToken cancellationToken)
        => Task.FromResult(new PluginToolResult("Use the screen's command buttons."));

    public async Task<JsonElement> ExecuteCommandAsync(string command, JsonElement arguments, CancellationToken cancellationToken)
    {
        if (command != "wait") throw new ArgumentException("Unknown command.");
        var label = arguments.GetProperty("label").GetString() ?? "request";
        var milliseconds = arguments.GetProperty("milliseconds").GetInt32();
        if (milliseconds is < 0 or > 10000) throw new ArgumentException("Delay must be between 0 and 10000 ms.");
        EventReceived?.Invoke(this, new("started", JsonSerializer.SerializeToElement(new { label })));
        await Task.Delay(milliseconds, cancellationToken);
        EventReceived?.Invoke(this, new("finished", JsonSerializer.SerializeToElement(new { label })));
        return JsonSerializer.SerializeToElement(new { label });
    }

    // A real server session should stop and join its background listener/workers here.
    public void Dispose() { }
}
