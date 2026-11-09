using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProtoBench.Core.Diagnostics;
using ProtoBench.Core.Documents;
using ProtoBench.Parsing.Abstractions;
using ProtoBench.Parsing.Engines;

namespace ProtoBench.Infrastructure.Logs;

/// <summary>Workspace-owned disposable disk index. All database work runs on a worker and is serialized.</summary>
public sealed partial class LogStore : ILogParseSink, IDisposable
{
    private readonly object gate = new();
    private readonly string path;
    private readonly FileStream lease;
    private static readonly string DefaultDirectory = PrepareCacheDirectory();
    private readonly SqliteConnection connection;
    private readonly SqliteCommand insert, insertField, insertFieldCheck;
    private readonly CancellationTokenSource lifetime = new();
    private readonly IProgress<LogImportProgress>? progress;
    private readonly List<ParseDiagnostic> diagnostics = [];
    private SqliteTransaction? transaction;
    private CancellationToken activeToken;
    private long lastProgress, diagnosticCount;
    private bool disposed;
    public long Count { get; private set; }
    public bool IsTruncated { get; private set; }
    public string DatabasePath => path;
    public IReadOnlyList<ParseDiagnostic> Diagnostics => diagnostics;
    public long DiagnosticErrors { get; private set; }
    public long DiagnosticWarnings { get; private set; }
    public long DiagnosticInformation { get; private set; }

    public LogStore(IProgress<LogImportProgress>? progress = null, string? temporaryDirectory = null)
    {
        this.progress = progress;
        path = Path.Combine(temporaryDirectory ?? DefaultDirectory, "protobench-log-" + Guid.NewGuid().ToString("N") + ".db");
        lease = new FileStream(path + ".lock", FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        try
        {
            connection.Open();
            connection.CreateFunction<string, string, bool>("contains_text", (value, search) =>
            {
                CheckCancellation();
                return value.Contains(search, StringComparison.OrdinalIgnoreCase);
            });
            connection.CreateFunction("keep_going", () => { CheckCancellation(); return true; });
            using var schema = connection.CreateCommand();
            schema.CommandText = """
                PRAGMA journal_mode=OFF;
                PRAGMA synchronous=OFF;
                PRAGMA temp_store=FILE;
                PRAGMA cache_size=-8192;
                CREATE TABLE records (
                  id INTEGER PRIMARY KEY, summary TEXT NOT NULL, payload BLOB NOT NULL, search TEXT NOT NULL,
                  record_sort TEXT NOT NULL, time_sort TEXT NOT NULL, stan TEXT NOT NULL, terminal TEXT NOT NULL,
                  acceptor TEXT NOT NULL, sequence TEXT NOT NULL, mti TEXT NOT NULL, direction TEXT NOT NULL,
                  role TEXT NOT NULL, profile TEXT NOT NULL, connection_id TEXT NOT NULL, time_ticks INTEGER NOT NULL);
                CREATE TABLE fields (record_id INTEGER NOT NULL, name TEXT NOT NULL, label TEXT NOT NULL,
                  element TEXT NOT NULL, value TEXT NOT NULL);
                CREATE TABLE field_checks (record_id INTEGER NOT NULL, column_key TEXT NOT NULL, severity INTEGER NOT NULL);
                """;
            schema.ExecuteNonQuery();
            transaction = connection.BeginTransaction();
            insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO records VALUES ($id,$summary,$payload,$search,$record,$time,$stan,$terminal,$acceptor,$sequence,$mti,$direction,$role,$profile,$connection,$ticks)";
            foreach (var name in new[] { "id", "summary", "payload", "search", "record", "time", "stan", "terminal", "acceptor", "sequence", "mti", "direction", "role", "profile", "connection", "ticks" })
                insert.Parameters.Add(new SqliteParameter("$" + name, name == "payload" ? SqliteType.Blob : name is "id" or "ticks" ? SqliteType.Integer : SqliteType.Text));
            insertField = connection.CreateCommand();
            insertField.CommandText = "INSERT INTO fields VALUES ($id,$name,$label,$element,$value)";
            foreach (var name in new[] { "id", "name", "label", "element", "value" }) insertField.Parameters.Add(new SqliteParameter("$" + name, name == "id" ? SqliteType.Integer : SqliteType.Text));
            insertFieldCheck = connection.CreateCommand();
            insertFieldCheck.CommandText = "INSERT INTO field_checks VALUES ($id,$key,$severity)";
            insertFieldCheck.Parameters.Add(new SqliteParameter("$id", SqliteType.Integer));
            insertFieldCheck.Parameters.Add(new SqliteParameter("$key", SqliteType.Text));
            insertFieldCheck.Parameters.Add(new SqliteParameter("$severity", SqliteType.Integer));
        }
        catch
        {
            connection.Dispose();
            File.Delete(path);
            lease.Dispose();
            lifetime.Dispose();
            throw;
        }
    }

    public async Task ImportAsync(ILogParser parser, Stream input, CancellationToken token)
    {
        activeToken = token;
        using var interrupt = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
        if (parser is IStreamingLogParser streaming) await streaming.ParseToAsync(input, this, token);
        else
        {
            // Legacy plugins own their allocation strategy; do not pass multi-gigabyte input to them.
            if (!input.CanSeek || input.Length > JsonDefinedParser.MaxPreviewBytes)
                throw new InvalidDataException("This plugin uses the preview parser API. Large logs require an IStreamingLogParser extension or a JSON/message profile.");
            var result = await parser.ParseAsync(input, token);
            foreach (var record in result.Records) { token.ThrowIfCancellationRequested(); AddRecord(record); }
            foreach (var diagnostic in result.Diagnostics) AddDiagnostic(diagnostic);
            IsTruncated = result.IsTruncated;
        }
        token.ThrowIfCancellationRequested();
        transaction!.Commit();
        transaction.Dispose();
        transaction = null;
        progress?.Report(new(input.CanSeek ? input.Length : lastProgress, Count, "Indexing"));
        using var indexes = connection.CreateCommand();
        indexes.CommandText = """
            CREATE INDEX record_order ON records(record_sort COLLATE NOCASE,id);
            CREATE INDEX time_order ON records(time_sort COLLATE NOCASE,id);
            CREATE INDEX stan_order ON records(stan COLLATE NOCASE,id);
            CREATE INDEX terminal_order ON records(terminal COLLATE NOCASE,id);
            CREATE INDEX acceptor_order ON records(acceptor COLLATE NOCASE,id);
            CREATE INDEX sequence_order ON records(sequence COLLATE NOCASE,id);
            CREATE INDEX mti_order ON records(mti COLLATE NOCASE,id);
            CREATE INDEX field_record ON fields(record_id);
            CREATE INDEX field_check_record ON field_checks(record_id);
            CREATE INDEX related_lookup ON records(profile,connection_id,stan,terminal,time_ticks);
            CREATE INDEX related_time ON records(profile,time_ticks);
            CREATE INDEX related_stan_time ON records(profile,stan,time_ticks);
            """;
        indexes.ExecuteNonQuery();
        token.ThrowIfCancellationRequested();
        if (diagnosticCount > diagnostics.Count)
            diagnostics.Add(new("parse.diagnostic-limit", $"{diagnosticCount:N0} diagnostics reported; showing the first 1,000.", DiagnosticSeverity.Warning));
        ReportProgress(input.CanSeek ? input.Length : lastProgress);
    }

    public void AddRecord(ParsedRecord record)
    {
        CheckCancellation();
        var id = Count + 1;
        var m = record.Message;
        var stan = MessageAnalysis.Value(record, "11");
        var terminal = MessageAnalysis.Value(record, "41");
        var acceptor = MessageAnalysis.Value(record, "42");
        var summaryMessage = m is null ? null : m with
        {
            ProfileId = Clip(m.ProfileId), SpecVersion = Clip(m.SpecVersion), RecordNumber = Clip(m.RecordNumber),
            LoggedDateTime = Clip(m.LoggedDateTime), Direction = Clip(m.Direction), ConnectionId = Clip(m.ConnectionId),
            Sequence = Clip(m.Sequence), MessageType = Clip(m.MessageType), Role = Clip(m.Role), Process = ""
        };
        var summary = new LogRecordSummary(id, record.LineNumber, summaryMessage, Clip(stan), Clip(terminal), Clip(acceptor),
            record.Findings.Count(f => f.Status is "Missing" or "Invalid" or "Forbidden"), record.Findings.Count);
        insert.Transaction = transaction;
        Set("id", id); Set("summary", JsonSerializer.Serialize(summary)); Set("payload", Pack(record));
        Set("search", record.RawText + "\n" + string.Join("\n", record.Fields.Select(f => f.Name +
            (record.RawText.Contains(f.Value, StringComparison.OrdinalIgnoreCase) ? "" : "\n" + f.Value))));
        Set("record", (long.TryParse(m?.RecordNumber, out var number) ? number : record.LineNumber).ToString("D20", CultureInfo.InvariantCulture));
        var time = MessageAnalysis.LoggedTime(record);
        Set("time", time?.ToString("MMddHHmmssfffffff", CultureInfo.InvariantCulture) ?? "");
        Set("stan", stan); Set("terminal", terminal); Set("acceptor", acceptor);
        Set("sequence", m?.Sequence.PadLeft(20, '0') ?? ""); Set("mti", m?.MessageType ?? "");
        Set("direction", m?.Direction ?? ""); Set("role", m?.Role ?? ""); Set("profile", m?.ProfileId ?? "");
        Set("connection", m?.ConnectionId ?? ""); Set("ticks", time?.Ticks ?? 0);
        insert.ExecuteNonQuery();
        insertField.Transaction = transaction;
        foreach (var field in record.Fields)
        {
            RememberTableColumn(field);
            insertField.Parameters["$id"].Value = id;
            insertField.Parameters["$name"].Value = field.Name.ToUpperInvariant();
            insertField.Parameters["$label"].Value = (field.OriginalLabel ?? "").ToUpperInvariant();
            insertField.Parameters["$element"].Value = (field.DataElement ?? "").ToUpperInvariant();
            insertField.Parameters["$value"].Value = field.Value;
            insertField.ExecuteNonQuery();
        }
        IndexTableChecks(record, id);
        Count = id;
        if (Count % 2000 == 0)
        {
            transaction!.Commit(); transaction.Dispose(); transaction = connection.BeginTransaction();
        }
        void Set(string name, object value) => insert.Parameters["$" + name].Value = value;
        static string Clip(string value) => value.Length <= 128 ? value : value[..128] + "…";
    }

    public void AddDiagnostic(ParseDiagnostic diagnostic)
    {
        diagnosticCount++;
        switch (diagnostic.Severity)
        {
            case DiagnosticSeverity.Error: DiagnosticErrors++; break;
            case DiagnosticSeverity.Warning: DiagnosticWarnings++; break;
            case DiagnosticSeverity.Information: DiagnosticInformation++; break;
        }
        if (diagnostics.Count < 1000) diagnostics.Add(diagnostic);
    }

    public void ReportProgress(long bytesRead)
    {
        if (bytesRead - lastProgress < 4 * 1024 * 1024) return;
        lastProgress = bytesRead;
        progress?.Report(new(bytesRead, Count));
    }

    public Task<LogPage> QueryAsync(LogQuery query, CancellationToken token = default) => RunAsync(() =>
    {
        if (query.PageSize is < 1 or > LogQuery.MaximumPageSize || query.PageIndex < 0) throw new ArgumentOutOfRangeException(nameof(query));
        var where = "keep_going()";
        if (query.Direction.Length > 0) where += " AND direction=$direction";
        if (query.Role.Length > 0) where += " AND role=$role";
        if (query.Field.Length > 0)
            where += " AND EXISTS (SELECT 1 FROM fields f WHERE f.record_id=r.id AND (f.name=$field OR f.label=$field OR f.element=$field) AND contains_text(f.value,$search))";
        else if (query.Search.Length > 0) where += " AND contains_text(r.search,$search)";
        using var command = connection.CreateCommand();
        command.CommandTimeout = 0;
        command.Parameters.AddWithValue("$direction", query.Direction);
        command.Parameters.AddWithValue("$role", query.Role);
        command.Parameters.AddWithValue("$field", query.Field.ToUpperInvariant());
        command.Parameters.AddWithValue("$search", query.Search);
        bool unfiltered = query.Direction.Length == 0 && query.Role.Length == 0 && query.Field.Length == 0 && query.Search.Length == 0;
        command.CommandText = $"SELECT COUNT(*) FROM records r WHERE {where}";
        long matches = unfiltered ? Count : (long)command.ExecuteScalar()!;
        var page = Math.Min(query.PageIndex, Math.Max(0, (matches - 1) / query.PageSize));
        var sort = query.Sort switch { "datetime" => "time_sort", "11" => "stan", "41" => "terminal", "42" => "acceptor", "sequence" => "sequence", "mti" => "mti", _ => "record_sort" };
        var order = query.Descending ? "DESC" : "ASC";
        command.CommandText = $"SELECT summary FROM records r WHERE {where} ORDER BY {sort} COLLATE NOCASE {order},id {order} LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$size", query.PageSize);
        command.Parameters.AddWithValue("$offset", checked(page * query.PageSize));
        var rows = new List<LogRecordSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) { CheckCancellation(); rows.Add(JsonSerializer.Deserialize<LogRecordSummary>(reader.GetString(0))!); }
        return new LogPage(rows, matches, Count, page, query.PageSize);
    }, token);

    public Task<ParsedRecord> ReadAsync(long id, CancellationToken token = default) => RunAsync(() => Read(id), token);

    public Task<IReadOnlyList<LogRecordSummary>> RelatedAsync(long id, ParsedRecord selected, RecordMessageParser? parser = null,
        CancellationToken token = default) => RunAsync<IReadOnlyList<LogRecordSummary>>(() =>
    {
        var correlation = selected.Correlation ?? parser?.GetCorrelation(selected.Message?.MessageType ?? "");
        if (correlation is null || !correlation.IsValid() || correlation.MessageTypes is not { Count: > 0 }
            || selected.Message is not { } m || MessageAnalysis.LoggedTime(selected) is not { } time) return [];
        var keys = correlation.Keys.Select(k => (Key: k, Value: MessageAnalysis.Value(selected, k))).ToArray();
        if (keys.Any(k => k.Value.Length == 0)) return [];
        using var command = connection.CreateCommand();
        command.CommandTimeout = 0;
        var conditions = new List<string> { "profile=$profile", "time_ticks BETWEEN $start AND $end",
            "direction<>$direction", "id<>$id", "keep_going()" };
        command.Parameters.AddWithValue("$profile", m.ProfileId);
        if (correlation.SameConnection)
        {
            conditions.Add("connection_id=$connection");
            command.Parameters.AddWithValue("$connection", m.ConnectionId);
        }
        for (var i = 0; i < keys.Length; i++)
        {
            var key = keys[i].Key;
            command.Parameters.AddWithValue("$v" + i, keys[i].Value);
            // Retain indexed summary lookups for common keys; arbitrary keys use
            // the record's raw-field index. No field names or values enter SQL text.
            if (key is "11" or "41") conditions.Add((key == "11" ? "stan" : "terminal") + "=$v" + i);
            else
            {
                command.Parameters.AddWithValue("$k" + i, key.ToUpperInvariant());
                conditions.Add($"EXISTS (SELECT 1 FROM fields f WHERE f.record_id=records.id "
                    + $"AND (f.element=$k{i} OR f.name=$k{i} OR f.label=$k{i}) AND f.value=$v{i})");
            }
        }
        command.CommandText = "SELECT id,summary,payload FROM records WHERE " + string.Join(" AND ", conditions) + " ORDER BY id";
        var ticks = TimeSpan.FromSeconds(correlation.WindowSeconds).Ticks;
        command.Parameters.AddWithValue("$start", time.Ticks - ticks); command.Parameters.AddWithValue("$end", time.Ticks + ticks);
        command.Parameters.AddWithValue("$direction", m.Direction); command.Parameters.AddWithValue("$id", id);
        var rows = new List<LogRecordSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            CheckCancellation();
            if (MessageAnalysis.Related(selected, [Unpack((byte[])reader[2])], correlation).Count == 0) continue;
            rows.Add(JsonSerializer.Deserialize<LogRecordSummary>(reader.GetString(1))!);
            if (rows.Count == 251) break; // One sentinel lets the UI explicitly report more matches.
        }
        return rows;
    }, token);

    private ParsedRecord Read(long id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM records WHERE id=$id";
        command.Parameters.AddWithValue("$id", id);
        var data = command.ExecuteScalar() as byte[] ?? throw new KeyNotFoundException("Message is no longer available.");
        return Unpack(data);
    }
    private Task<T> RunAsync<T>(Func<T> action, CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activeToken = token;
            CheckCancellation();
            using var interrupt = token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
            using var shutdown = lifetime.Token.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
            try { return action(); }
            catch (SqliteException) when (token.IsCancellationRequested || lifetime.IsCancellationRequested) { throw new OperationCanceledException(token); }
        }
    }, token);
    private void CheckCancellation() { lifetime.Token.ThrowIfCancellationRequested(); activeToken.ThrowIfCancellationRequested(); }
    private static byte[] Pack(ParsedRecord record)
    {
        using var stream = new MemoryStream();
        using (var compressor = new BrotliStream(stream, CompressionLevel.Fastest, true)) JsonSerializer.Serialize(compressor, record);
        return stream.ToArray();
    }
    private static ParsedRecord Unpack(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var decompressor = new BrotliStream(stream, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<ParsedRecord>(decompressor)!;
    }
    public void Dispose()
    {
        lifetime.Cancel();
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            transaction?.Dispose(); insert.Dispose(); insertField.Dispose(); insertFieldCheck.Dispose(); connection.Dispose();
            try { File.Delete(path); }
            finally { lease.Dispose(); }
        }
    }

    private static string PrepareCacheDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ProtoBench", "logs");
        Directory.CreateDirectory(directory);
        // A crashed/reloaded process releases its DeleteOnClose lease. Never touch a live workspace's index.
        foreach (var candidate in Directory.EnumerateFiles(directory, "protobench-log-*.db"))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(candidate)["protobench-log-".Length..], "N", out _) || File.Exists(candidate + ".lock")) continue;
            try
            {
                using (new FileStream(candidate, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(candidate);
                File.Delete(candidate + "-journal");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return directory;
    }
}
