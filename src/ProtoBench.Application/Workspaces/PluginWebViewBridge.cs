using System.Text.Json;

namespace ProtoBench.Application.Workspaces;

/// <summary>Validates web messages before using the same execution and disposal path as standard forms.</summary>
public sealed class PluginWebViewBridge(PluginToolWorkspace workspace)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<string> ReceiveAsync(string message)
    {
        string? id = null;
        try
        {
            if (message.Length > 1_048_576) throw new ArgumentException("Message exceeds 1 MiB of text.");
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            id = root.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128) throw new ArgumentException("Supply a string request id of 1–128 characters.");
            switch (root.GetProperty("type").GetString())
            {
                case "cancel": workspace.Cancel(); return Reply("ack", true);
                case "clear":
                    if (workspace.IsBusy) throw new ArgumentException("Tool is busy.");
                    workspace.Clear();
                    return Reply("ack", true);
                case "execute":
                    if (workspace.IsBusy) throw new ArgumentException("Tool is busy.");
                    var inputs = root.GetProperty("inputs").Deserialize<Dictionary<string, string>>()
                        ?? throw new ArgumentException("Supply an inputs object.");
                    // Validate the complete request before changing any inputs.
                    foreach (var input in inputs)
                        if (!workspace.Inputs.ContainsKey(input.Key) || input.Value is null)
                            throw new ArgumentException($"Unknown input or non-string value: {input.Key}");
                    foreach (var input in inputs) workspace.SetInput(input.Key, input.Value);
                    await workspace.ExecuteAsync();
                    return JsonSerializer.Serialize(new { type = "result", id, success = workspace.Result is not null, result = workspace.Result, status = workspace.Status }, Json);
                default: throw new ArgumentException("Unknown message type. Use execute, cancel or clear.");
            }
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            return Reply("error", false, ex.Message);
        }

        string Reply(string type, bool success, string? error = null) => JsonSerializer.Serialize(new { type, id, success, error }, Json);
    }
}
