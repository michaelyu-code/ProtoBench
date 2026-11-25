using System.Text.Json;
using ProtoBench.Core.Documents;

namespace ProtoBench.Infrastructure.Configuration;

public sealed class JsonTodoStore(string path) : ITodoStore
{
    public TodoData Load()
    {
        if (!File.Exists(path)) return new();
        using var input = File.OpenRead(path);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("Task storage exceeds the 16 MiB limit.");
        var data = JsonSerializer.Deserialize<TodoData>(input) ?? throw new InvalidDataException("Task storage is empty.");
        if (data.SchemaVersion != 1 || data.Items is null || data.RetentionDays is < 1 or > 3650 ||
            data.Items.Any(t => t is null || t.Id == Guid.Empty || string.IsNullOrWhiteSpace(t.Title) || t.Title.Length > 200 ||
                t.Notes is null || t.Notes.Length > 10000 || t.Steps is null || t.Steps.Any(s => s is null || s.Text is null || s.Text.Length > 200) ||
                t.TimeboxMinutes is < 1 or > 480 || !double.IsFinite(t.RemainingSeconds) || t.RemainingSeconds < 0 ||
                (t.CompletedAt is not null && t.Deadline is not null)) ||
            data.Items.Select(t => t.Id).Distinct().Count() != data.Items.Count || data.Items.Count(t => t.Deadline is not null) > 1)
            throw new InvalidDataException("Task storage is invalid or has an unsupported version. The file has been preserved.");
        return data;
    }
    public void Save(TodoData data)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, data, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            if (File.Exists(full)) File.Replace(temporary, full, null);
            else File.Move(temporary, full);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
