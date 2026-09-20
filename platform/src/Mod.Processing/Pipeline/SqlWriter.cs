using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Resilience;
using Mod.Platform.Common.Sql;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Mod.Processing.Pipeline;

internal sealed class SqlWriter(SqlConnectionFactory sqlFactory, ProcessingOptions options)
{
    private readonly ResiliencePipeline _pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 5,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromSeconds(1),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder().Handle<SqlException>()
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            BreakDuration = TimeSpan.FromSeconds(30),
            MinimumThroughput = 3,
            FailureRatio = 1.0,
            SamplingDuration = TimeSpan.FromSeconds(60),
            ShouldHandle = new PredicateBuilder().Handle<SqlException>()
        })
        .Build();

    public async Task WriteAsync(BatchWriteContext ctx, CancellationToken ct)
    {
        await _pipeline.ExecuteAsync(async token =>
        {
            await using var conn = sqlFactory.CreateConnection();
            await conn.OpenAsync(token);
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(token);

            var now = DateTime.UtcNow;

            await BulkInsertRawEventsAsync(conn, tx, ctx, now, token);
            await InsertProcessedEventsAsync(conn, tx, ctx, now, token);
            await InsertDeadLettersAsync(conn, tx, ctx, now, token);
            await ProcessGapsAsync(conn, tx, ctx, now, token);
            await RecomputeRollupsAsync(conn, tx, ctx, now, token);
            await UpsertCollectorFreshnessAsync(conn, tx, ctx, now, token);
            await UpsertReconciliationAsync(conn, tx, ctx, now, token);

            await tx.CommitAsync(token);
        }, ct);
    }

    // ── Raw events ──────────────────────────────────────────────────────────

    private static async Task BulkInsertRawEventsAsync(
        SqlConnection conn, SqlTransaction tx, BatchWriteContext ctx, DateTime now, CancellationToken ct)
    {
        if (ctx.Valid.Count == 0) return;

        var dt = BuildRawEventTable();
        foreach (var ae in ctx.Valid)
        {
            var e = ae.Event;
            dt.Rows.Add(
                e.EventId, ae.Mapping.TenantId, ae.Mapping.SiteId,
                ctx.Message.CollectorId, e.SourceId, ae.Mapping.AssetId,
                e.Sequence,
                e.EventTime.UtcDateTime, e.ObservedTime.UtcDateTime,
                e.EventType.ToString(), e.Payload.GetRawText(),
                e.Quality.ToString(), ctx.Message.BatchId,
                ctx.Message.ReceivedAt.UtcDateTime, now);
        }

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx)
        {
            DestinationTableName = "telemetry.RawEvent",
            BatchSize = 1000
        };
        MapRawEventColumns(bulk);
        await bulk.WriteToServerAsync(dt, ct);
    }

    private static DataTable BuildRawEventTable()
    {
        var dt = new DataTable();
        dt.Columns.Add("EventId", typeof(Guid));
        dt.Columns.Add("TenantId", typeof(Guid));
        dt.Columns.Add("SiteId", typeof(Guid));
        dt.Columns.Add("CollectorId", typeof(Guid));
        dt.Columns.Add("SourceId", typeof(string));
        dt.Columns.Add("AssetId", typeof(Guid));
        dt.Columns.Add("Sequence", typeof(long));
        dt.Columns.Add("EventTime", typeof(DateTime));
        dt.Columns.Add("ObservedTime", typeof(DateTime));
        dt.Columns.Add("EventType", typeof(string));
        dt.Columns.Add("PayloadJson", typeof(string));
        dt.Columns.Add("Quality", typeof(string));
        dt.Columns.Add("BatchId", typeof(Guid));
        dt.Columns.Add("ReceivedAt", typeof(DateTime));
        dt.Columns.Add("ProcessedAt", typeof(DateTime));
        return dt;
    }

    private static void MapRawEventColumns(SqlBulkCopy bulk)
    {
        foreach (DataColumn col in BuildRawEventTable().Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
    }

    // ── ProcessedEvent ───────────────────────────────────────────────────────

    private static async Task InsertProcessedEventsAsync(
        SqlConnection conn, SqlTransaction tx, BatchWriteContext ctx, DateTime now, CancellationToken ct)
    {
        var ids = ctx.Valid.Select(ae => ae.Event.EventId)
            .Concat(ctx.DeadLetters.Where(dl => dl.EventId.HasValue).Select(dl => dl.EventId!.Value))
            .Distinct()
            .ToList();
        if (ids.Count == 0) return;

        var dt = new DataTable();
        dt.Columns.Add("EventId", typeof(Guid));
        dt.Columns.Add("ProcessedAt", typeof(DateTime));
        foreach (var id in ids) dt.Rows.Add(id, now);

        using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.Default, tx)
        {
            DestinationTableName = "telemetry.ProcessedEvent",
            BatchSize = 1000
        };
        bulk.ColumnMappings.Add("EventId", "EventId");
        bulk.ColumnMappings.Add("ProcessedAt", "ProcessedAt");
        await bulk.WriteToServerAsync(dt, ct);
    }

    // ── Dead letters ─────────────────────────────────────────────────────────

    private static async Task InsertDeadLettersAsync(
        SqlConnection conn, SqlTransaction tx, BatchWriteContext ctx, DateTime now, CancellationToken ct)
    {
        if (ctx.DeadLetters.Count == 0) return;

        const string sql = """
            INSERT INTO telemetry.DeadLetter
                (TenantId, SiteId, CollectorId, BatchId, EventId,
                 ReasonCode, ReasonDetail, EventJson,
                 ReceivedAt, CreatedAt, Status, StatusChangedAt, ReplayAttempts)
            VALUES
                (@TenantId, @SiteId, @CollectorId, @BatchId, @EventId,
                 @ReasonCode, @ReasonDetail, @EventJson,
                 @ReceivedAt, @CreatedAt, 'New', @CreatedAt, 0)
            """;

        foreach (var dl in ctx.DeadLetters)
        {
            await conn.ExecuteAsync(new CommandDefinition(sql, new
            {
                dl.TenantId,
                dl.SiteId,
                ctx.Message.CollectorId,
                ctx.Message.BatchId,
                dl.EventId,
                dl.ReasonCode,
                dl.ReasonDetail,
                dl.EventJson,
                ReceivedAt = dl.ReceivedAt.UtcDateTime,
                CreatedAt = now
            }, tx, cancellationToken: ct));
        }
    }

    // ── Gap detection ────────────────────────────────────────────────────────

    private static async Task ProcessGapsAsync(
        SqlConnection conn, SqlTransaction tx, BatchWriteContext ctx, DateTime now, CancellationToken ct)
    {
        if (ctx.Valid.Count == 0) return;

        // Group by (CollectorId, SourceId)
        var groups = ctx.Valid
            .GroupBy(ae => (ctx.Message.CollectorId, ae.Event.SourceId, ae.Mapping.TenantId))
            .ToList();

        foreach (var group in groups)
        {
            var (collectorId, sourceId, tenantId) = group.Key;
            var sequences = group.Select(ae => ae.Event.Sequence).OrderBy(s => s).ToList();
            var maxSeq = sequences[^1];

            // Lock the SourceSequence row
            var lastSeq = await conn.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition(
                    "SELECT LastSequence FROM telemetry.SourceSequence WITH (UPDLOCK) " +
                    "WHERE CollectorId = @collectorId AND SourceId = @sourceId",
                    new { collectorId, sourceId }, tx, cancellationToken: ct));

            var currentLast = lastSeq ?? 0L;

            foreach (var seq in sequences)
            {
                if (seq > currentLast + 1)
                {
                    // Gap detected
                    await conn.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO telemetry.SequenceGap
                            (TenantId, CollectorId, SourceId, FromSequence, ToSequence, DetectedAt)
                        VALUES
                            (@tenantId, @collectorId, @sourceId, @from, @to, @now)
                        """,
                        new { tenantId, collectorId, sourceId, from = currentLast + 1, to = seq - 1, now },
                        tx, cancellationToken: ct));
                    currentLast = seq;
                }
                else if (seq <= (lastSeq ?? 0L))
                {
                    // Late / out-of-order arrival — check if it fills an open gap
                    await TryFillGapAsync(conn, tx, collectorId, sourceId, seq, now, ct);
                }
                else
                {
                    currentLast = seq;
                }
            }

            // Upsert SourceSequence
            await conn.ExecuteAsync(new CommandDefinition("""
                MERGE telemetry.SourceSequence WITH (HOLDLOCK) AS t
                USING (SELECT @collectorId AS CollectorId, @sourceId AS SourceId) AS s
                    ON t.CollectorId = s.CollectorId AND t.SourceId = s.SourceId
                WHEN MATCHED THEN
                    UPDATE SET LastSequence = CASE WHEN @maxSeq > t.LastSequence THEN @maxSeq ELSE t.LastSequence END,
                               UpdatedAt = @now
                WHEN NOT MATCHED THEN
                    INSERT (CollectorId, SourceId, TenantId, LastSequence, UpdatedAt)
                    VALUES (@collectorId, @sourceId, @tenantId, @maxSeq, @now);
                """,
                new { collectorId, sourceId, tenantId, maxSeq, now },
                tx, cancellationToken: ct));
        }
    }

    private static async Task TryFillGapAsync(
        SqlConnection conn, SqlTransaction tx,
        Guid collectorId, string sourceId, long sequence, DateTime now, CancellationToken ct)
    {
        var gaps = await conn.QueryAsync<(long GapId, long From, long To)>(
            new CommandDefinition("""
                SELECT GapId, FromSequence, ToSequence
                FROM telemetry.SequenceGap
                WHERE CollectorId = @collectorId AND SourceId = @sourceId
                  AND FilledAt IS NULL
                  AND @sequence BETWEEN FromSequence AND ToSequence
                """,
                new { collectorId, sourceId, sequence }, tx, cancellationToken: ct));

        foreach (var (gapId, from, to) in gaps)
        {
            var expectedCount = to - from + 1;
            var actualCount = await conn.QuerySingleAsync<long>(
                new CommandDefinition("""
                    SELECT COUNT(DISTINCT Sequence)
                    FROM telemetry.RawEvent
                    WHERE CollectorId = @collectorId AND SourceId = @sourceId
                      AND Sequence BETWEEN @from AND @to
                    """,
                    new { collectorId, sourceId, from, to }, tx, cancellationToken: ct));

            if (actualCount >= expectedCount)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE telemetry.SequenceGap SET FilledAt = @now WHERE GapId = @gapId",
                    new { now, gapId }, tx, cancellationToken: ct));
            }
        }
    }

    // ── Rollup recompute ─────────────────────────────────────────────────────

    private async Task RecomputeRollupsAsync(
        SqlConnection conn, SqlTransaction tx, BatchWriteContext ctx, DateTime now, CancellationToken ct)
    {
        if (ctx.Valid.Count == 0) return;

        var pairs = ctx.Valid
            .Select(ae => (ae.Mapping.AssetId, ae.Mapping.TenantId, ae.Mapping.SiteId, ae.Mapping.LineId,
                           MinuteStart: FloorToMinute(ae.Event.EventTime)))
            .Distinct()
            .ToList();

        foreach (var (assetId, tenantId, siteId, lineId, minuteStart) in pairs)
        {
            await RollupComputer.RecomputeAsync(
                conn, tx, assetId, tenantId, siteId, lineId, minuteStart,
                options.WindowGraceSeconds, now, ct);
        }
    }

    private static DateTime FloorToMinute(DateTimeOffset t)
    {
        var u = t.UtcDateTime;
        return new DateTime(u.Year, u.Month, u.Day, u.Hour, u.Minute, 0, DateTimeKind.Utc);
    }

    // ── CollectorFreshness ───────────────────────────────────────────────────

    private static async Task UpsertCollectorFreshnessAsync(
        SqlConnection conn, SqlTransaction tx, BatchWriteContext ctx, DateTime now, CancellationToken ct)
    {
        var validCount = ctx.Valid.Count;
        var dlCount = ctx.DeadLetters.Count;
        if (validCount == 0 && dlCount == 0) return;

        DateTime? maxEventTime = ctx.Valid.Count > 0
            ? ctx.Valid.Max(ae => ae.Event.EventTime.UtcDateTime)
            : (DateTime?)null;

        await conn.ExecuteAsync(new CommandDefinition("""
            MERGE telemetry.CollectorFreshness WITH (HOLDLOCK) AS t
            USING (SELECT @collectorId AS CollectorId) AS s ON t.CollectorId = s.CollectorId
            WHEN MATCHED THEN UPDATE SET
                LastEventTime = CASE
                    WHEN @maxEventTime IS NOT NULL AND @maxEventTime > t.LastEventTime
                    THEN @maxEventTime ELSE t.LastEventTime END,
                LastReceivedAt = @receivedAt,
                LastProcessedAt = @now,
                EventsProcessedTotal = t.EventsProcessedTotal + @validCount,
                EventsDeadLetteredTotal = t.EventsDeadLetteredTotal + @dlCount
            WHEN NOT MATCHED THEN INSERT
                (CollectorId, TenantId, SiteId, LastEventTime, LastReceivedAt, LastProcessedAt,
                 EventsProcessedTotal, EventsDeadLetteredTotal)
            VALUES
                (@collectorId, @tenantId, @siteId, @maxEventTime,
                 @receivedAt, @now, @validCount, @dlCount);
            """,
            new
            {
                ctx.Message.CollectorId,
                ctx.Message.TenantId,
                ctx.Message.SiteId,
                maxEventTime,
                receivedAt = ctx.Message.ReceivedAt.UtcDateTime,
                now,
                validCount,
                dlCount
            }, tx, cancellationToken: ct));
    }

    // ── Reconciliation ───────────────────────────────────────────────────────

    private static async Task UpsertReconciliationAsync(
        SqlConnection conn, SqlTransaction tx, BatchWriteContext ctx, DateTime now, CancellationToken ct)
    {
        // Group valid + dead-letter counts by (collectorId, minute of eventTime)
        var minutes = new Dictionary<DateTime, (int Accepted, int DeadLettered)>();

        foreach (var ae in ctx.Valid)
        {
            var min = FloorToMinute(ae.Event.EventTime);
            minutes[min] = minutes.TryGetValue(min, out var v)
                ? (v.Accepted + 1, v.DeadLettered)
                : (1, 0);
        }
        foreach (var dl in ctx.DeadLetters)
        {
            // Use ReceivedAt minute for dead letters that have no eventTime
            var min = new DateTime(
                dl.ReceivedAt.UtcDateTime.Year, dl.ReceivedAt.UtcDateTime.Month,
                dl.ReceivedAt.UtcDateTime.Day, dl.ReceivedAt.UtcDateTime.Hour,
                dl.ReceivedAt.UtcDateTime.Minute, 0, DateTimeKind.Utc);
            minutes[min] = minutes.TryGetValue(min, out var v)
                ? (v.Accepted, v.DeadLettered + 1)
                : (0, 1);
        }

        const string sql = """
            MERGE telemetry.Reconciliation WITH (HOLDLOCK) AS t
            USING (SELECT @collectorId AS CollectorId, @minuteStart AS MinuteStart) AS s
                ON t.CollectorId = s.CollectorId AND t.MinuteStart = s.MinuteStart
            WHEN MATCHED THEN UPDATE SET
                AcceptedCount = t.AcceptedCount + @accepted,
                DeadLetteredCount = t.DeadLetteredCount + @deadLettered
            WHEN NOT MATCHED THEN INSERT
                (CollectorId, MinuteStart, TenantId, AcceptedCount, DeadLetteredCount)
            VALUES
                (@collectorId, @minuteStart, @tenantId, @accepted, @deadLettered);
            """;

        foreach (var (min, (accepted, dead)) in minutes)
        {
            await conn.ExecuteAsync(new CommandDefinition(sql,
                new
                {
                    ctx.Message.CollectorId,
                    minuteStart = min,
                    ctx.Message.TenantId,
                    accepted,
                    deadLettered = dead
                }, tx, cancellationToken: ct));
        }
    }

    private static DateTime FloorToMinute(DateTime t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, DateTimeKind.Utc);
}
