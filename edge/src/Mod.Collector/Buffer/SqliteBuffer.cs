using Microsoft.Data.Sqlite;
using Mod.Collector.Health;

namespace Mod.Collector.Buffer;

internal sealed record BufferedEvent(long RowId, string SourceId, long Sequence, string Json, int SizeBytes);

internal sealed record MinuteCount(string MinuteStart, long Produced, long DroppedAtEdge);

internal sealed class SqliteBuffer : IDisposable
{
    private readonly SqliteConnection _db;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly MinuteCounter _minute;
    private readonly ILogger<SqliteBuffer> _logger;

    private volatile int _capacityEvents;
    private long _overflowDroppedTotal;
    private bool _bufferOverflowEver;
    private DateTimeOffset _currentMinuteStart;

    public long OverflowDroppedTotal => Interlocked.Read(ref _overflowDroppedTotal);
    public bool BufferOverflowEver => _bufferOverflowEver;

    public SqliteBuffer(CollectorOptions options, MinuteCounter minute, ILogger<SqliteBuffer> logger)
    {
        _minute = minute;
        _logger = logger;
        _capacityEvents = 200_000;
        _currentMinuteStart = TruncateToMinute(DateTimeOffset.UtcNow);

        Directory.CreateDirectory(options.DataDir);
        var path = Path.Combine(options.DataDir, "buffer.db");
        _db = new SqliteConnection($"Data Source={path}");
        _db.Open();
        Execute("PRAGMA journal_mode=WAL");
        Execute("PRAGMA synchronous=NORMAL");
        InitSchema();

        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT value FROM buffer_state WHERE key='bufferOverflowEver'";
        _bufferOverflowEver = cmd.ExecuteScalar() is string v && v == "true";
    }

    public void SetCapacity(int capacity) => _capacityEvents = Math.Max(1, capacity);

    public async Task AppendAsync(
        string sourceId,
        string eventType,
        DateTimeOffset eventTime,
        Func<long, string> makeJson,
        CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var tx = _db.BeginTransaction();

            MaybeFlushMinuteCounts(tx);

            using var seqCmd = _db.CreateCommand();
            seqCmd.Transaction = tx;
            seqCmd.CommandText = """
                INSERT INTO source_sequence(sourceId, lastSequence) VALUES(@s, 1)
                ON CONFLICT(sourceId) DO UPDATE SET lastSequence = lastSequence + 1
                RETURNING lastSequence
                """;
            seqCmd.Parameters.AddWithValue("@s", sourceId);
            var seq = (long)seqCmd.ExecuteScalar()!;

            var json = makeJson(seq);
            var sizeBytes = System.Text.Encoding.UTF8.GetByteCount(json);

            var count = CountEvents(tx);
            if (count >= _capacityEvents)
            {
                var deleted = DeleteOldestOfType(tx, "ProcessValue");
                if (!deleted) DeleteOldest(tx);
                Interlocked.Increment(ref _overflowDroppedTotal);
                _minute.RecordDroppedAtEdge();
                if (!_bufferOverflowEver)
                {
                    _bufferOverflowEver = true;
                    using var flagCmd = _db.CreateCommand();
                    flagCmd.Transaction = tx;
                    flagCmd.CommandText = "INSERT OR REPLACE INTO buffer_state(key,value) VALUES('bufferOverflowEver','true')";
                    flagCmd.ExecuteNonQuery();
                }
            }

            using var ins = _db.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO buffer(sourceId,sequence,eventTime,eventType,json,sizeBytes)
                VALUES(@sid,@seq,@et,@etype,@json,@sz)
                """;
            ins.Parameters.AddWithValue("@sid", sourceId);
            ins.Parameters.AddWithValue("@seq", seq);
            ins.Parameters.AddWithValue("@et", eventTime.UtcDateTime.ToString("O"));
            ins.Parameters.AddWithValue("@etype", eventType);
            ins.Parameters.AddWithValue("@json", json);
            ins.Parameters.AddWithValue("@sz", sizeBytes);
            ins.ExecuteNonQuery();

            tx.Commit();
            _minute.RecordProduced();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<BufferedEvent>> PeekBatchAsync(int maxEvents, int maxBytes, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT rowid,sourceId,sequence,json,sizeBytes FROM buffer ORDER BY rowid LIMIT @n";
            cmd.Parameters.AddWithValue("@n", maxEvents);
            using var reader = cmd.ExecuteReader();

            var result = new List<BufferedEvent>();
            var totalBytes = 0;
            while (reader.Read())
            {
                var sz = reader.GetInt32(4);
                if (result.Count > 0 && totalBytes + sz > maxBytes) break;
                result.Add(new BufferedEvent(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetString(3),
                    sz));
                totalBytes += sz;
            }
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteBatchAsync(IEnumerable<long> rowIds, CancellationToken ct)
    {
        var ids = string.Join(',', rowIds);
        if (string.IsNullOrEmpty(ids)) return;
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = $"DELETE FROM buffer WHERE rowid IN ({ids})";
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordRejectedBatchAsync(string batchId, string reason, int eventCount, string json, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                INSERT INTO rejected_batches(batchId,reason,eventCount,rejectedAt,json)
                VALUES(@bid,@r,@ec,@at,@json)
                """;
            cmd.Parameters.AddWithValue("@bid", batchId);
            cmd.Parameters.AddWithValue("@r", reason);
            cmd.Parameters.AddWithValue("@ec", eventCount);
            cmd.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("@json", json);
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordExecutedRequestAsync(string batchId, DateTimeOffset sentAt, int? statusCode, bool success, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var tx = _db.BeginTransaction();
            using var ins = _db.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO executed_requests(batchId,sentAt,statusCode,success)
                VALUES(@bid,@sat,@sc,@ok)
                """;
            ins.Parameters.AddWithValue("@bid", batchId);
            ins.Parameters.AddWithValue("@sat", sentAt.UtcDateTime.ToString("O"));
            ins.Parameters.AddWithValue("@sc", statusCode.HasValue ? (object)statusCode.Value : DBNull.Value);
            ins.Parameters.AddWithValue("@ok", success ? 1 : 0);
            ins.ExecuteNonQuery();

            using var trim = _db.CreateCommand();
            trim.Transaction = tx;
            trim.CommandText = """
                DELETE FROM executed_requests
                WHERE rowid NOT IN (
                    SELECT rowid FROM executed_requests ORDER BY rowid DESC LIMIT 1000
                )
                """;
            trim.ExecuteNonQuery();
            tx.Commit();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM buffer";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DateTimeOffset?> GetOldestEventTimeAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT eventTime FROM buffer ORDER BY rowid LIMIT 1";
            var result = cmd.ExecuteScalar();
            if (result is string s && DateTimeOffset.TryParse(s, out var dt))
                return dt;
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Returns unacknowledged completed minute counts, oldest first, up to max entries.
    public async Task<List<MinuteCount>> GetUnacknowledgedMinuteCountsAsync(int max, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = """
                SELECT minuteStart, produced, droppedAtEdge
                FROM minute_counts
                WHERE acknowledged = 0
                ORDER BY minuteStart
                LIMIT @max
                """;
            cmd.Parameters.AddWithValue("@max", max);
            using var reader = cmd.ExecuteReader();
            var result = new List<MinuteCount>();
            while (reader.Read())
                result.Add(new MinuteCount(reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AcknowledgeMinuteCountsAsync(IEnumerable<string> minuteStarts, CancellationToken ct)
    {
        var starts = minuteStarts.ToList();
        if (starts.Count == 0) return;
        await _lock.WaitAsync(ct);
        try
        {
            using var tx = _db.BeginTransaction();
            foreach (var ms in starts)
            {
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE minute_counts SET acknowledged=1 WHERE minuteStart=@ms";
                cmd.Parameters.AddWithValue("@ms", ms);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        finally
        {
            _lock.Release();
        }
    }

    // Returns stored reply JSON for a command, or null if not found.
    public async Task<string?> GetCommandReplyAsync(string requestId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "SELECT reply FROM command_replies WHERE requestId=@id";
            cmd.Parameters.AddWithValue("@id", requestId);
            return cmd.ExecuteScalar() as string;
        }
        finally
        {
            _lock.Release();
        }
    }

    // Persists a command reply, trimming to the last 1 000 entries.
    public async Task SaveCommandReplyAsync(string requestId, string replyJson, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var tx = _db.BeginTransaction();
            using var ins = _db.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT OR REPLACE INTO command_replies(requestId,reply,createdAt)
                VALUES(@id,@reply,@at)
                """;
            ins.Parameters.AddWithValue("@id", requestId);
            ins.Parameters.AddWithValue("@reply", replyJson);
            ins.Parameters.AddWithValue("@at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
            ins.ExecuteNonQuery();

            using var trim = _db.CreateCommand();
            trim.Transaction = tx;
            trim.CommandText = """
                DELETE FROM command_replies
                WHERE requestId NOT IN (
                    SELECT requestId FROM command_replies ORDER BY createdAt DESC LIMIT 1000
                )
                """;
            trim.ExecuteNonQuery();
            tx.Commit();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _lock.Dispose();
        _db.Dispose();
    }

    // --- private helpers ---

    private void InitSchema()
    {
        Execute("""
            CREATE TABLE IF NOT EXISTS buffer(
                rowid INTEGER PRIMARY KEY AUTOINCREMENT,
                sourceId TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                eventTime TEXT NOT NULL,
                eventType TEXT NOT NULL,
                json TEXT NOT NULL,
                sizeBytes INTEGER NOT NULL
            )
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS source_sequence(
                sourceId TEXT PRIMARY KEY,
                lastSequence INTEGER NOT NULL
            )
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS minute_counts(
                minuteStart TEXT NOT NULL,
                produced INTEGER NOT NULL DEFAULT 0,
                droppedAtEdge INTEGER NOT NULL DEFAULT 0,
                acknowledged INTEGER NOT NULL DEFAULT 0
            )
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS rejected_batches(
                rowid INTEGER PRIMARY KEY AUTOINCREMENT,
                batchId TEXT NOT NULL,
                reason TEXT NOT NULL,
                eventCount INTEGER NOT NULL,
                rejectedAt TEXT NOT NULL,
                json TEXT NOT NULL
            )
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS executed_requests(
                rowid INTEGER PRIMARY KEY AUTOINCREMENT,
                batchId TEXT NOT NULL,
                sentAt TEXT NOT NULL,
                statusCode INTEGER,
                success INTEGER NOT NULL
            )
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS buffer_state(
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            )
            """);
        Execute("""
            CREATE TABLE IF NOT EXISTS command_replies(
                requestId TEXT PRIMARY KEY,
                reply TEXT NOT NULL,
                createdAt TEXT NOT NULL
            )
            """);
    }

    private void Execute(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private int CountEvents(SqliteTransaction tx)
    {
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM buffer";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private bool DeleteOldestOfType(SqliteTransaction tx, string eventType)
    {
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            DELETE FROM buffer WHERE rowid = (
                SELECT rowid FROM buffer WHERE eventType=@t ORDER BY rowid LIMIT 1
            )
            """;
        cmd.Parameters.AddWithValue("@t", eventType);
        return cmd.ExecuteNonQuery() > 0;
    }

    private void DeleteOldest(SqliteTransaction tx)
    {
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM buffer WHERE rowid = (SELECT rowid FROM buffer ORDER BY rowid LIMIT 1)";
        cmd.ExecuteNonQuery();
    }

    private void MaybeFlushMinuteCounts(SqliteTransaction tx)
    {
        var now = DateTimeOffset.UtcNow;
        var minuteStart = TruncateToMinute(now);
        if (minuteStart <= _currentMinuteStart) return;

        var (produced, dropped) = _minute.Flush();
        if (produced > 0 || dropped > 0)
        {
            using var cmd = _db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO minute_counts(minuteStart,produced,droppedAtEdge)
                VALUES(@ms,@p,@d)
                """;
            cmd.Parameters.AddWithValue("@ms", _currentMinuteStart.UtcDateTime.ToString("O"));
            cmd.Parameters.AddWithValue("@p", produced);
            cmd.Parameters.AddWithValue("@d", dropped);
            cmd.ExecuteNonQuery();
        }
        _currentMinuteStart = minuteStart;
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset dt) =>
        new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, TimeSpan.Zero);
}
