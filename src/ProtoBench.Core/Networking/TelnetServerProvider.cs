namespace ProtoBench.Core.Networking;

/// <summary>A server profile. The factory must return a fresh session for each connection.</summary>
public sealed record TelnetServerProvider(string Id, string Name, string Description,
    Func<ITelnetServerSession> CreateSession);

public interface ITelnetServerSession : IDisposable
{
    /// <summary>Own Telnet negotiation and framing. Honor cancellation and return when the client disconnects.</summary>
    Task RunAsync(ITelnetServerConnection connection, CancellationToken cancellationToken);
}

/// <summary>Application bytes after TLS decryption. The host owns transport, capture and idle timeout.</summary>
public interface ITelnetServerConnection
{
    /// <summary>Read application bytes; zero means end of stream. Reads have a five-minute idle timeout.</summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
    void Log(string message);
}
