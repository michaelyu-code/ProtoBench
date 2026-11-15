using System.Text;

namespace ProtoBench.Utilities.Networking;

public enum RelayDirection { None, ClientToServer, ServerToClient }
public sealed record RelayCaptureEntry(long Sequence, DateTimeOffset Timestamp, long ConnectionId,
    RelayDirection Direction, string Description, ReadOnlyMemory<byte> Data)
{
    public string Summary => $"{Sequence} · {Timestamp:HH:mm:ss.fff} UTC · Connection {ConnectionId} · " +
        (Direction switch { RelayDirection.ClientToServer => "Client → Server", RelayDirection.ServerToClient => "Server → Client", _ => "Event" }) +
        $" · {(Data.Length > 0 ? $"{Data.Length:N0} bytes" : Description)}";
    public string Hex => Convert.ToHexString(Data.Span);
    public string Text => Encoding.UTF8.GetString(Data.Span);
}

public sealed record RelayCaptureSnapshot(IReadOnlyList<RelayCaptureEntry> Entries, long EvictedEntries,
    long ClientBytes, long ServerBytes);

/// <summary>A bounded, thread-safe capture ring. Eviction never interrupts forwarding.</summary>
public sealed class RelayCaptureBuffer(int maximumEntries = 1024, int maximumBytes = 4 * 1024 * 1024)
{
    private readonly object gate = new();
    private readonly Queue<RelayCaptureEntry> entries = new();
    private int retainedBytes;
    private long sequence, evicted, clientBytes, serverBytes;

    public void Add(long connectionId, RelayDirection direction, string description, ReadOnlySpan<byte> data = default)
    {
        if (maximumEntries < 1 || maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        lock (gate)
        {
            if (direction == RelayDirection.ClientToServer) clientBytes += data.Length;
            if (direction == RelayDirection.ServerToClient) serverBytes += data.Length;
            var nextSequence = ++sequence;
            if (data.Length > maximumBytes) { evicted++; return; }
            var entry = new RelayCaptureEntry(nextSequence, DateTimeOffset.UtcNow, connectionId, direction, description, data.ToArray());
            entries.Enqueue(entry); retainedBytes += data.Length;
            while (entries.Count > maximumEntries || retainedBytes > maximumBytes)
            {
                retainedBytes -= entries.Dequeue().Data.Length;
                evicted++;
            }
        }
    }

    public RelayCaptureSnapshot Snapshot()
    {
        lock (gate) return new(entries.ToArray(), evicted, clientBytes, serverBytes);
    }

    public void Clear()
    {
        lock (gate) { entries.Clear(); retainedBytes = 0; evicted = 0; }
    }
}
