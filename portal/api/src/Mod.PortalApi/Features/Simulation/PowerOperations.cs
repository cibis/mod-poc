// DEMO SCAFFOLDING
using Microsoft.AspNetCore.SignalR;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;
using Mod.PortalApi.Features.Simulation.Channel;
using Mod.PortalApi.Features.Simulation.Data;
using Mod.PortalApi.Features.Simulation.Hubs;
using Mod.PortalApi.Features.Simulation.Provisioning;

namespace Mod.PortalApi.Features.Simulation;

internal sealed record PowerResult(bool Success, string? Reason);

// Encapsulates the multi-step power-on and power-off operations for a single collector.
// Registered as scoped; injected from endpoints and (via IServiceScope) from ScenarioRunner.
internal sealed class PowerOperations(
    ICollectorService collectorService,
    IEnrolmentTokenService tokenService,
    ICertificateService certService,
    IAuditWriter audit,
    SimServiceBusClient? sbClient,
    ContainerAppProvisioner? provisioner,
    SimRepository repo,
    SimStateCache stateCache,
    IHubContext<SimHub> hub)
{
    // Max concurrent power-ons across all instances (process-wide limit = 5).
    private static readonly SemaphoreSlim _powerOnSemaphore = new(5, 5);

    internal async Task<PowerResult> PowerOnAsync(
        Guid collectorId, Actor actor, CancellationToken ct = default)
    {
        // 1. Verify collector exists and has eligible status.
        var detail = await collectorService.GetCollectorDetailAsync(collectorId);
        if (detail is null)
            return new PowerResult(false, "Collector not found");

        var status = detail.Collector.Status;
        if (status is not ("Registered" or "Enrolled"))
            return new PowerResult(false, $"Collector status {status} is not eligible for power-on");

        if (!await _powerOnSemaphore.WaitAsync(TimeSpan.Zero, ct))
            return new PowerResult(false, "Maximum concurrent power-on operations reached (5)");

        try
        {
            // 2. Issue enrolment token.
            var tokenResult = await tokenService.IssueTokenAsync(collectorId, actor);

            string commandSas = string.Empty;
            string statusSas = string.Empty;

            if (sbClient is not null)
            {
                // 3. Ensure sim/{collectorId} queue exists; mint SAS tokens (30 days).
                await sbClient.EnsureCollectorQueueAsync(collectorId, ct);
                var queueName = SimServiceBusClient.CollectorQueueName(collectorId);
                commandSas = sbClient.MintListenToken(queueName, TimeSpan.FromDays(30));
                statusSas = sbClient.MintSendToken("sim-status", TimeSpan.FromDays(30));
            }

            // 4. Determine container app name and update SQL state to Provisioning.
            var appName = ContainerAppProvisioner.AppName(collectorId);
            await repo.SetPowerOnStateAsync(collectorId, appName);
            stateCache.SetProvisioningState(collectorId, "Provisioning");

            await hub.Clients.All.SendAsync("provisioning",
                new { collectorId, provisioningState = "Provisioning" }, ct);

            // Write Power timeline marker.
            await repo.InsertTimelineMarkerAsync("Power",
                $"Power on {collectorId:D}", [collectorId], null);

            await audit.WriteAsync(actor, "SimulatorPowerOn", "Collector",
                collectorId.ToString("D"), detail.Collector.TenantId, null);

            if (provisioner is not null)
            {
                // 5. Start ARM operation in background; poll for completion.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var operation = await provisioner.StartCreateAsync(
                            collectorId, detail.Collector.TenantId, detail.Collector.SiteId,
                            commandSas, statusSas);

                        while (!operation.HasCompleted)
                        {
                            await Task.Delay(5000);
                            await operation.UpdateStatusAsync();
                        }

                        await repo.SetProvisioningStateAsync(collectorId, "Running", null);
                        stateCache.SetProvisioningState(collectorId, "Running");
                        await hub.Clients.All.SendAsync("provisioning",
                            new { collectorId, provisioningState = "Running" });
                    }
                    catch (Exception ex)
                    {
                        var msg = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                        await repo.SetProvisioningStateAsync(collectorId, "Failed", msg);
                        stateCache.SetProvisioningState(collectorId, "Failed");
                        await hub.Clients.All.SendAsync("provisioning",
                            new { collectorId, provisioningState = "Failed", lastError = msg });
                    }
                });
            }
            else
            {
                // No provisioner configured: mark Running immediately (local dev).
                await repo.SetProvisioningStateAsync(collectorId, "Running", null);
                stateCache.SetProvisioningState(collectorId, "Running");
                await hub.Clients.All.SendAsync("provisioning",
                    new { collectorId, provisioningState = "Running" }, ct);
            }

            return new PowerResult(true, null);
        }
        finally
        {
            _powerOnSemaphore.Release();
        }
    }

    internal async Task PowerOffAsync(
        Guid collectorId, Actor actor, CancellationToken ct = default)
    {
        var stateRow = (await repo.GetAllCollectorStatesAsync())
            .FirstOrDefault(s => s.CollectorId == collectorId);

        var appName = stateRow?.ContainerAppName
            ?? ContainerAppProvisioner.AppName(collectorId);

        // Delete container app.
        if (provisioner is not null)
        {
            try { await provisioner.DeleteAsync(appName, ct); }
            catch (Exception ex)
            {
                // Log but don't block power-off.
                _ = ex;
            }
        }

        // Delete sim/{collectorId} queue.
        if (sbClient is not null)
        {
            try { await sbClient.DeleteCollectorQueueAsync(collectorId, ct); }
            catch { /* best effort */ }
        }

        // Revoke active certificates (without changing collector status).
        try { await certService.RevokeAllActiveAsync(collectorId, "SimulatorPowerOff"); }
        catch { /* best effort */ }

        // Update SQL state and cache.
        await repo.SetPowerOffStateAsync(collectorId);
        stateCache.SetProvisioningState(collectorId, "Off");

        await repo.InsertTimelineMarkerAsync("Power",
            $"Power off {collectorId:D}", [collectorId], null);

        await hub.Clients.All.SendAsync("provisioning",
            new { collectorId, provisioningState = "Off" }, ct);

        // Best-effort audit.
        try
        {
            var detail = await collectorService.GetCollectorDetailAsync(collectorId);
            if (detail is not null)
                await audit.WriteAsync(actor, "SimulatorPowerOff", "Collector",
                    collectorId.ToString("D"), detail.Collector.TenantId, null);
        }
        catch { /* best effort */ }
    }
}
