using Dapper;
using Mod.Platform.Common.Sql;

namespace Mod.ManagementApi.Housekeeping;

public sealed class HealthHistoryPurge(SqlConnectionFactory db, ILogger<HealthHistoryPurge> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await PurgeAsync(stoppingToken);
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = db.CreateConnection();
            var deleted = await conn.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM registry.CollectorHealthHistory WHERE ReceivedAt < @cutoff",
                    new { cutoff = DateTime.UtcNow.AddDays(-7) },
                    cancellationToken: ct));
            if (deleted > 0)
                log.LogInformation("HealthHistoryPurge: deleted {Count} rows", deleted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "HealthHistoryPurge failed");
        }
    }
}
