using System.Text.Json;

namespace ProtoBench.PluginSdk;

public enum PluginCommandConcurrency { Sequential = 0, Parallel = 1 }

/// <summary>An unsolicited session event. Data must remain valid during event delivery.</summary>
public sealed record PluginToolEvent(string Name, JsonElement? Data = null);

/// <summary>Optional command channel for custom screens. Commands do not change the standard
/// form's inputs or result. Sequential is the safe default; parallel sessions must coordinate
/// their own shared resources. Events may be raised from any thread until disposal.</summary>
public interface IPluginCommandSession : IPluginToolSession
{
    PluginCommandConcurrency CommandConcurrency => PluginCommandConcurrency.Sequential;
    event EventHandler<PluginToolEvent>? EventReceived;
    Task<JsonElement> ExecuteCommandAsync(string command, JsonElement arguments, CancellationToken cancellationToken);
}
