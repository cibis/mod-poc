using System.Text.Json;
using Dapper;
using Mod.Platform.Common.Sql;
using Mod.Processing.Pipeline;

namespace Mod.Processing.Replay;

/// <summary>
/// Runs only on the leader replica (partition 0 owner). Every 10 s, picks up to 500
/// ReplayRequested dead letters and re-runs the processing pipeline for each event.
/// BatchUnreadable rows are discarded rather than replayed.
/// </summary>
internal sealed class DeadLetterReplayService(
    SqlConnectionFactory sqlFactory,
    SqlWriter sqlWriter,
    LeaderState leaderState,
    ILogger<DeadLetterReplayService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            if (!leaderState.IsLeader) continue;

            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Dead-letter replay cycle failed"); }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var conn = sqlFactory.CreateConnection();
        await conn.OpenAsync(ct);

        var rows = (await conn.QueryAsync<DeadLetterRow>(
            new CommandDefinition("""
                SELECT TOP 500 DeadLetterId, TenantId, SiteId, CollectorId, BatchId,
                               EventId, ReasonCode, EventJson, ReceivedAt, ReplayAttempts
                FROM telemetry.DeadLetter
                WHERE Status = 'ReplayRequested'
                ORDER BY DeadLetterId
                """, cancellationToken: ct))).ToList();

        foreach (var row in rows)
        {
            if (ct.IsCancellationRequested) break;
            await ProcessRowAsync(row, ct);
        }
    }

    private async Task ProcessRowAsync(DeadLetterRow row, CancellationToken ct)
    {
        // BatchUnreadable cannot be replayed
        if (row.ReasonCode == "BatchUnreadable")
        {
            await UpdateStatusAsync(row.DeadLetterId, "Discarded", row.ReplayAttempts + 1,
                "BatchUnreadable rows cannot be replayed", ct);
            return;
        }

        try
        {
            // Parse the stored event JSON
            using var doc = JsonDocument.Parse(row.EventJson);
            var el = doc.RootElement;
            var receivedAt = row.ReceivedAt;

            var (evt, code, detail) = EventParser.Parse(el, receivedAt);

            if (evt is null)
            {
                // Validation still fails — update reason and revert to New
                await UpdateStatusWithReasonAsync(row.DeadLetterId, "New", row.ReplayAttempts + 1,
                    code!, detail, ct);
                return;
            }

            // Remove from ProcessedEvent so the write step won't skip it
            await RemoveFromProcessedEventAsync(evt.EventId, ct);

            // Build a minimal write context with just this one event
            // Attribution will be re-attempted by mapping cache
            var msgCtx = new MessageContext(
                row.CollectorId, row.TenantId ?? Guid.Empty,
                row.SiteId ?? Guid.Empty, row.BatchId, receivedAt);

            // We re-run attribution inline
            var mappingCache = new MappingCache(sqlFactory, new ProcessingOptions(30, 0, 30));
            var mapping = await mappingCache.GetAsync(row.CollectorId, evt.SourceId, ct);

            if (mapping is null || mapping.TenantId != (row.TenantId ?? Guid.Empty))
            {
                var reason = mapping is null ? "UnmappedSource" : "UnmappedSource";
                var replayDetail = mapping is null ? null : "TenantMismatch";
                await UpdateStatusWithReasonAsync(row.DeadLetterId, "New", row.ReplayAttempts + 1,
                    reason, replayDetail, ct);
                return;
            }

            var writeCtx = new BatchWriteContext(
                msgCtx,
                [new AttributedEvent(evt, mapping)],
                []);

            await sqlWriter.WriteAsync(writeCtx, ct);

            // Mark the original dead letter as Replayed
            await UpdateStatusAsync(row.DeadLetterId, "Replayed", row.ReplayAttempts + 1, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Replay failed for DeadLetterId={Id}", row.DeadLetterId);
            await UpdateStatusWithReasonAsync(row.DeadLetterId, "New", row.ReplayAttempts + 1,
                "ReplayError", ex.Message[..Math.Min(ex.Message.Length, 200)], ct);
        }
    }

    private async Task RemoveFromProcessedEventAsync(Guid eventId, CancellationToken ct)
    {
        await using var conn = sqlFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM telemetry.ProcessedEvent WHERE EventId = @eventId",
            new { eventId }, cancellationToken: ct));
    }

    private async Task UpdateStatusAsync(
        long deadLetterId, string status, int attempts, string? detail, CancellationToken ct)
    {
        await using var conn = sqlFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE telemetry.DeadLetter
            SET Status = @status, StatusChangedAt = @now, ReplayAttempts = @attempts,
                ReasonDetail = COALESCE(@detail, ReasonDetail)
            WHERE DeadLetterId = @deadLetterId
            """,
            new { deadLetterId, status, now = DateTime.UtcNow, attempts, detail },
            cancellationToken: ct));
    }

    private async Task UpdateStatusWithReasonAsync(
        long deadLetterId, string status, int attempts,
        string reasonCode, string? detail, CancellationToken ct)
    {
        await using var conn = sqlFactory.CreateConnection();
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE telemetry.DeadLetter
            SET Status = @status, StatusChangedAt = @now, ReplayAttempts = @attempts,
                ReasonCode = @reasonCode, ReasonDetail = @detail
            WHERE DeadLetterId = @deadLetterId
            """,
            new { deadLetterId, status, now = DateTime.UtcNow, attempts, reasonCode, detail },
            cancellationToken: ct));
    }

    // Class (not record) so Dapper uses property mapping — nullable Guid? columns work correctly.
    private sealed class DeadLetterRow
    {
        public long DeadLetterId { get; set; }
        public Guid? TenantId { get; set; }
        public Guid? SiteId { get; set; }
        public Guid CollectorId { get; set; }
        public Guid BatchId { get; set; }
        public Guid? EventId { get; set; }
        public string ReasonCode { get; set; } = "";
        public string EventJson { get; set; } = "";
        public DateTime ReceivedAt { get; set; }
        public int ReplayAttempts { get; set; }
    }
}
