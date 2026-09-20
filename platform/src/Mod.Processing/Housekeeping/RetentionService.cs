using Dapper;
using Mod.Platform.Common.Sql;

namespace Mod.Processing.Housekeeping;

/// <summary>
/// Runs hourly on the leader replica. Deletes old RawEvent and ProcessedEvent rows in batches
/// to avoid long-running transactions.
/// </summary>
internal sealed class RetentionService(
    SqlConnectionFactory sqlFactory,
    RetentionOptions options,
    LeaderState leaderState,
    ILogger<RetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private const int BatchSize = 10_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            if (!leaderState.IsLeader) continue;

            try { await RunCycleAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Retention cycle failed"); }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var rawCutoff = DateTime.UtcNow.AddDays(-options.RawRetentionDays);
        var dedupeCutoff = DateTime.UtcNow.AddDays(-options.DedupeRetentionDays);

        logger.LogInformation(
            "Retention: deleting RawEvent before {Raw} and ProcessedEvent before {Dedupe}",
            rawCutoff, dedupeCutoff);

        await DeleteInBatchesAsync(
            "DELETE TOP (@batch) FROM telemetry.RawEvent WHERE ReceivedAt < @cutoff",
            rawCutoff, ct);

        await DeleteInBatchesAsync(
            "DELETE TOP (@batch) FROM telemetry.ProcessedEvent WHERE ProcessedAt < @cutoff",
            dedupeCutoff, ct);
    }

    private async Task DeleteInBatchesAsync(string sql, DateTime cutoff, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await using var conn = sqlFactory.CreateConnection();
            var deleted = await conn.ExecuteAsync(
                new CommandDefinition(sql, new { batch = BatchSize, cutoff }, cancellationToken: ct));
            if (deleted < BatchSize) break;
            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }
}
