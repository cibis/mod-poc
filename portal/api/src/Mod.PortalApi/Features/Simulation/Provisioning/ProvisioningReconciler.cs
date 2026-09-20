// DEMO SCAFFOLDING
using Microsoft.AspNetCore.SignalR;
using Mod.PortalApi.Features.Simulation.Data;
using Mod.PortalApi.Features.Simulation.Hubs;

namespace Mod.PortalApi.Features.Simulation.Provisioning;

// At startup and every 60 s: lists container apps tagged mod-sim-collector, compares with
// sim.CollectorState, and corrects state discrepancies.
internal sealed class ProvisioningReconciler(
    ContainerAppProvisioner provisioner,
    SimRepository repo,
    SimStateCache stateCache,
    IHubContext<SimHub> hub,
    ILogger<ProvisioningReconciler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pre-populate cache from SQL before starting the loop.
        try
        {
            var rows = await repo.GetAllCollectorStatesAsync();
            stateCache.InitFromSql(rows);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not pre-populate sim state cache from SQL");
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
            var armApps = await provisioner.ListSimCollectorAppsAsync(ct);
            var sqlStates = await repo.GetAllCollectorStatesAsync();

            var armByCollector = armApps.ToDictionary(a => a.CollectorId, a => a.AppName);
            var sqlByCollector = sqlStates.ToDictionary(s => s.CollectorId);

            // ARM app exists but no SQL state row → orphan; delete the app.
            foreach (var (collectorId, appName) in armByCollector)
            {
                if (!sqlByCollector.ContainsKey(collectorId))
                {
                    logger.LogWarning(
                        "Orphan container app {App} for unknown collector {CollectorId}; deleting",
                        appName, collectorId);
                    try { await provisioner.DeleteAsync(appName, ct); }
                    catch (Exception ex) { logger.LogWarning(ex, "Could not delete orphan app {App}", appName); }
                }
            }

            // SQL state is Running but no ARM app → mark Off.
            foreach (var state in sqlStates)
            {
                if (state.ProvisioningState == "Running" && !armByCollector.ContainsKey(state.CollectorId))
                {
                    logger.LogWarning(
                        "Collector {CollectorId} has Running state but no container app; marking Off",
                        state.CollectorId);
                    await repo.SetPowerOffStateAsync(state.CollectorId);
                    stateCache.SetProvisioningState(state.CollectorId, "Off");
                    await hub.Clients.All.SendAsync("provisioning",
                        new { collectorId = state.CollectorId, provisioningState = "Off" }, ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Provisioning reconciler error");
        }
    }
}
