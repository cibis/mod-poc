using Dapper;
using Microsoft.Data.SqlClient;

namespace Mod.PortalApi.Infrastructure.ServiceBus;

internal sealed class QueueReconciler(
    SqlConfig sqlConfig,
    IQueueManager? queueManager,
    ILogger<QueueReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (queueManager is null)
        {
            logger.LogWarning("CMD_SB_FQDN not configured; queue reconciler disabled.");
            return;
        }

        await ReconcileAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await ReconcileAsync(stoppingToken);
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = new SqlConnection(sqlConfig.ConnectionString);

            var activeCollectors = (await conn.QueryAsync<(Guid TenantId, Guid CollectorId)>(
                """
                SELECT TenantId, CollectorId
                FROM registry.Collector
                WHERE CommandChannelEnabled = 1 AND Status IN ('Registered', 'Enrolled')
                """)).AsList();

            foreach (var (tenantId, collectorId) in activeCollectors)
            {
                try { await queueManager!.EnsureQueuesAsync(tenantId, collectorId, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not ensure queues for collector {CollectorId}", collectorId); }
            }

            var inactiveCollectors = (await conn.QueryAsync<(Guid TenantId, Guid CollectorId)>(
                """
                SELECT TenantId, CollectorId
                FROM registry.Collector
                WHERE CommandChannelEnabled = 0 OR Status IN ('Revoked', 'Retired')
                """)).AsList();

            foreach (var (tenantId, collectorId) in inactiveCollectors)
            {
                try { await queueManager!.DeleteQueuesAsync(tenantId, collectorId, ct); }
                catch (Exception ex) { logger.LogWarning(ex, "Could not delete queues for collector {CollectorId}", collectorId); }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex) { logger.LogError(ex, "Queue reconciler error"); }
    }
}
