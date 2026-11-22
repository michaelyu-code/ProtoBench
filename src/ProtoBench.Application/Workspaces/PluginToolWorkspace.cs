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
    private PluginToolResult? result;
    private bool disposed, sessionDisposed;
    public string Output { get => output; private set => SetProperty(ref output, value); }
    public IReadOnlyList<ParsedField> Fields { get => fields; private set => SetProperty(ref fields, value); }
    public PluginToolResult? Result { get => result; private set => SetProperty(ref result, value); }

    /// <summary>UI input edits invalidate results so old interpretations cannot be mistaken for current ones.</summary>
    public void SetInput(string id, string value)
    {
        if (disposed || IsBusy || !Inputs.ContainsKey(id) || Inputs[id] == value) return;
        Inputs[id] = value;
        var hadResult = Result is not null;
        ResetResult();
        if (hadResult) Status = "Inputs changed. Run again to update the result.";
    }

    private void ResetResult() { Output = ""; Fields = []; Result = null; }

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
            ResetResult();
            try
            {
                foreach (var input in Definition.Inputs)
                {
                    var value = snapshot.GetValueOrDefault(input.Id) ?? "";
                    if (input.Required && string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{input.Label} is required.");
                    if (input.Kind == ToolInputKind.Choice && !input.Choices!.Contains(value)) throw new ArgumentException($"Invalid choice for {input.Label}.");
                    if (input.Kind == ToolInputKind.Integer && (!string.IsNullOrWhiteSpace(value) || input.Required))
                    {
                        if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
                            (input.Minimum is { } min && number < min) || (input.Maximum is { } max && number > max))
                            throw new ArgumentException($"{input.Label} must be an integer in the allowed range ({input.Minimum?.ToString() ?? "unbounded"} to {input.Maximum?.ToString() ?? "unbounded"}).");
                    }
                    if (input.Kind == ToolInputKind.FieldValues && ToolInputValues.ParseFieldValues(value).Count == 0 && input.Required)
                        throw new ArgumentException($"{input.Label} requires at least one supporting field.");
                }
                var result = await Task.Run(() => Session.ExecuteAsync(snapshot, token), token);
                token.ThrowIfCancellationRequested();
                if (result.Search is not null && result.Decoding is not null)
                    throw new ArgumentException("A tool result cannot contain both search and decoding presentations.");
                if (!disposed) { Output = result.Text; Fields = result.Fields ?? []; Result = result; }
            }
            finally { if (disposed) DisposeSession(); }
        });
    }

    public void Clear()
    {
        if (IsBusy || disposed) return;
        foreach (var input in Definition.Inputs) Inputs[input.Id] = input.DefaultValue;
        ResetResult();
        Status = "Ready";
    }

    public override void Dispose()
    {
        if (disposed) return;
        disposed = true;
        base.Dispose();
        Inputs.Clear();
        ResetResult();
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
