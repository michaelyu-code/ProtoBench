using ProtoBench.PluginSdk;
using ProtoBench.Core.Fields;

namespace ProtoBench.Application.Workspaces;

/// <summary>Owns one session; closing a running tab cancels before disposing its session.</summary>
public sealed class PluginToolWorkspace : ToolWorkspace
{
    public PluginTool Definition { get; }
    public IPluginToolSession Session { get; }
    public override string ToolId => Definition.Id;
    public override string Title => Definition.Name;
    public Dictionary<string, string> Inputs { get; } = new(StringComparer.Ordinal);
    private string output = "";
    private IReadOnlyList<ParsedField> fields = [];
    private bool disposed, sessionDisposed;
    public string Output { get => output; private set => SetProperty(ref output, value); }
    public IReadOnlyList<ParsedField> Fields { get => fields; private set => SetProperty(ref fields, value); }

    public PluginToolWorkspace(PluginTool definition)
    {
        Definition = definition;
        foreach (var input in definition.Inputs) Inputs.Add(input.Id, input.DefaultValue);
        Session = definition.CreateSession() ?? throw new InvalidOperationException("Plugin returned no session.");
    }

    public Task ExecuteAsync()
    {
        if (disposed || IsBusy) return Task.CompletedTask;
        var snapshot = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(Inputs));
        return RunAsync(async token =>
        {
            Output = "";
            Fields = [];
            try
            {
                foreach (var input in Definition.Inputs)
                {
                    var value = snapshot.GetValueOrDefault(input.Id) ?? "";
                    if (input.Required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{input.Label} is required.");
                    if (input.Kind == ToolInputKind.Choice && !input.Choices!.Contains(value)) throw new ArgumentException($"Invalid choice for {input.Label}.");
                }
                var result = await Task.Run(() => Session.ExecuteAsync(snapshot, token), token);
                token.ThrowIfCancellationRequested();
                if (!disposed) { Output = result.Text; Fields = result.Fields ?? []; }
            }
            finally { if (disposed) DisposeSession(); }
        });
    }

    public void Clear()
    {
        if (IsBusy || disposed) return;
        foreach (var input in Definition.Inputs) Inputs[input.Id] = input.DefaultValue;
        Output = "";
        Fields = [];
        Status = "Ready";
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        base.Dispose();
        Inputs.Clear();
        Output = "";
        Fields = [];
        if (!IsBusy) DisposeSession();
    }
    private void DisposeSession()
    {
        if (sessionDisposed) return;
        sessionDisposed = true;
        try { Session.Dispose(); } catch { }
    }
}

public sealed class PluginToolWorkspaceFactory(PluginTool tool) : IToolWorkspaceFactory
{
    public string ToolId => tool.Id;
    public ToolWorkspace Create() => new PluginToolWorkspace(tool);
}
