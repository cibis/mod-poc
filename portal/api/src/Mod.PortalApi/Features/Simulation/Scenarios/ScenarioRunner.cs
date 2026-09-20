// DEMO SCAFFOLDING
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Mod.PortalApi.Features.Simulation.Channel;
using Mod.PortalApi.Features.Simulation.Data;
using Mod.PortalApi.Features.Simulation.Hubs;
using Mod.PortalApi.Features.Simulation.Models;

namespace Mod.PortalApi.Features.Simulation.Scenarios;

// Executes scenario definitions sequentially (one at a time).
// Powers on target collectors first, then fires timed steps.
internal sealed class ScenarioRunner(
    SimRepository repo,
    SimStateCache stateCache,
    SimServiceBusClient? sbClient,
    IHubContext<SimHub> hub,
    IServiceScopeFactory scopeFactory,
    ILogger<ScenarioRunner> logger) : BackgroundService
{
    private volatile RunState? _current;

    // Returns a scenarioRunId, or null if another run is already active.
    internal async Task<Guid?> StartAsync(
        string scenarioName, Dictionary<string, object?> parameters, string startedBy,
        CancellationToken ct = default)
    {
        if (_current is not null) return null;

        var def = ScenarioLoader.Find(scenarioName);
        if (def is null) throw new ArgumentException($"Unknown scenario: {scenarioName}");

        var runId = Guid.NewGuid();
        var paramsJson = JsonSerializer.Serialize(parameters);
        await repo.InsertScenarioRunAsync(runId, scenarioName, paramsJson, startedBy);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _current = new RunState(runId, scenarioName, cts);

        _ = RunAsync(def, parameters, runId, cts.Token);
        return runId;
    }

    internal async Task StopAsync(Guid runId)
    {
        var state = _current;
        if (state?.RunId == runId)
        {
            await state.Cts.CancelAsync();
        }
    }

    internal RunState? CurrentRun => _current;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    private async Task RunAsync(
        ScenarioDefinition def, Dictionary<string, object?> parameters,
        Guid runId, CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Scenario {Name} run {RunId} starting", def.Name, runId);
            await repo.InsertTimelineMarkerAsync("ScenarioStart", $"Scenario {def.Name} started",
                null, runId);
            await hub.Clients.All.SendAsync("scenarioRun", BuildRunEvent(runId, def.Name, "Running", null));

            // Power on any target collectors that are Off and wait for enrolment.
            var targetIds = await ResolveAllTargetsAsync(def, parameters, ct);
            await EnsurePoweredOnAsync(targetIds, ct);

            var start = DateTime.UtcNow;
            int stepIndex = 0;

            foreach (var step in def.Steps.OrderBy(s => s.AtSeconds))
            {
                var fireAt = start.AddSeconds(step.AtSeconds);
                var delay = fireAt - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                ct.ThrowIfCancellationRequested();

                var stepTargets = await ResolveStepTargetsAsync(step.Targets, parameters, ct);
                stepIndex++;
                var label = BuildStepLabel(step, stepIndex);

                await repo.InsertTimelineMarkerAsync("ScenarioStep", label, stepTargets, runId);
                await hub.Clients.All.SendAsync("timelineMarker",
                    new { kind = "ScenarioStep", label, targets = stepTargets, scenarioRunId = runId });

                if (step.Action == "send-command" && sbClient is not null && step.CommandType is not null)
                {
                    foreach (var collectorId in stepTargets)
                    {
                        var cmd = new SimCommandBody(Guid.NewGuid(), step.CommandType,
                            DateTime.UtcNow, step.CommandArgs);
                        try { await sbClient.SendCommandAsync(collectorId, cmd, ct); }
                        catch (Exception ex) { logger.LogWarning(ex, "Could not send command to {Id}", collectorId); }
                    }
                }
                else if (step.Action == "power")
                {
                    await PowerTargetsAsync(stepTargets, step.Power == true, ct);
                }
            }

            // Run duration: wait until scenario end.
            var remaining = start.AddSeconds(def.DurationSeconds) - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining, ct);

            await FinishRunAsync(runId, def.Name, "Completed");
        }
        catch (OperationCanceledException)
        {
            // Restore link up and unpaused for all affected collectors.
            await RestoreAffectedAsync(def, parameters, CancellationToken.None);
            await FinishRunAsync(runId, def.Name, "Stopped");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Scenario {Name} run {RunId} failed", def.Name, runId);
            await FinishRunAsync(runId, def.Name, "Failed");
        }
        finally
        {
            _current = null;
        }
    }

    private async Task FinishRunAsync(Guid runId, string name, string status)
    {
        var endedAt = DateTime.UtcNow;
        await repo.UpdateScenarioRunAsync(runId, status, endedAt);
        await repo.InsertTimelineMarkerAsync($"ScenarioEnd",
            $"Scenario {name} {status.ToLowerInvariant()}", null, runId);
        await hub.Clients.All.SendAsync("scenarioRun",
            BuildRunEvent(runId, name, status, endedAt));
    }

    private async Task EnsurePoweredOnAsync(IReadOnlyList<Guid> collectorIds, CancellationToken ct)
    {
        // Use PowerOperations via a scope (it depends on scoped admin services).
        await using var scope = scopeFactory.CreateAsyncScope();
        var power = scope.ServiceProvider.GetRequiredService<PowerOperations>();

        var offIds = collectorIds.Where(id =>
            stateCache.GetProvisioningState(id) is "Off" or "Failed").ToList();

        foreach (var id in offIds)
        {
            try { await power.PowerOnAsync(id, SimActor, ct); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not power on {Id} for scenario", id); }
        }

        if (offIds.Count == 0) return;

        // Wait up to 2 min for enrolled status.
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var allEnrolled = offIds.All(id =>
                stateCache.GetStatus(id)?.Enrolled == true);
            if (allEnrolled) break;
            await Task.Delay(3000, ct);
        }
    }

    private async Task PowerTargetsAsync(
        IReadOnlyList<Guid> collectorIds, bool on, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var power = scope.ServiceProvider.GetRequiredService<PowerOperations>();
        foreach (var id in collectorIds)
        {
            try
            {
                if (on) await power.PowerOnAsync(id, SimActor, ct);
                else await power.PowerOffAsync(id, SimActor, ct);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Scenario power {On} failed for {Id}", on, id); }
        }
    }

    private async Task RestoreAffectedAsync(
        ScenarioDefinition def, Dictionary<string, object?> parameters, CancellationToken ct)
    {
        if (sbClient is null) return;
        var targets = await ResolveAllTargetsAsync(def, parameters, ct);
        foreach (var id in targets)
        {
            var linkUp = new SimCommandBody(Guid.NewGuid(), "SetLink", DateTime.UtcNow,
                new Dictionary<string, object?> { ["state"] = "up" });
            var unpause = new SimCommandBody(Guid.NewGuid(), "SetPaused", DateTime.UtcNow,
                new Dictionary<string, object?> { ["paused"] = false });
            try { await sbClient.SendCommandAsync(id, linkUp, ct); } catch { /* best effort */ }
            try { await sbClient.SendCommandAsync(id, unpause, ct); } catch { /* best effort */ }
        }
    }

    private async Task<IReadOnlyList<Guid>> ResolveAllTargetsAsync(
        ScenarioDefinition def, Dictionary<string, object?> parameters, CancellationToken ct)
    {
        // Collect all collector IDs referenced in any step of this scenario.
        var ids = new HashSet<Guid>();
        foreach (var step in def.Steps)
        {
            var stepIds = await ResolveStepTargetsAsync(step.Targets, parameters, ct);
            foreach (var id in stepIds) ids.Add(id);
        }
        return ids.ToList();
    }

    private async Task<IReadOnlyList<Guid>> ResolveStepTargetsAsync(
        StepTargetSpec? spec, Dictionary<string, object?> parameters, CancellationToken ct)
    {
        if (spec is null) return [];

        await using var scope = scopeFactory.CreateAsyncScope();
        var collectorSvc = scope.ServiceProvider
            .GetRequiredService<Features.Admin.Services.ICollectorService>();

        var all = await GetAllCollectorsAsync(collectorSvc);
        IEnumerable<Features.Admin.Services.CollectorSummary> filtered = all;

        if (spec.All == true) { /* no filter */ }
        else if (spec.AllPowered == true)
        {
            filtered = all.Where(c =>
                stateCache.GetProvisioningState(c.CollectorId) is "Running" or "Provisioning");
        }
        else if (spec.CollectorParamName is not null &&
                 parameters.TryGetValue(spec.CollectorParamName, out var pv) &&
                 pv is not null && Guid.TryParse(pv.ToString(), out var specId))
        {
            filtered = all.Where(c => c.CollectorId == specId);
        }
        else if (spec.RegionLabelParamName is not null &&
                 parameters.TryGetValue(spec.RegionLabelParamName, out var rpv))
        {
            var region = rpv?.ToString() ?? spec.RegionLabel;
            filtered = all.Where(c => c.RegionLabel == region);
        }
        else if (spec.RegionLabel is not null)
        {
            filtered = all.Where(c => c.RegionLabel == spec.RegionLabel);
        }

        return filtered.Select(c => c.CollectorId).ToList();
    }

    private static async Task<IReadOnlyList<Features.Admin.Services.CollectorSummary>> GetAllCollectorsAsync(
        Features.Admin.Services.ICollectorService svc)
    {
        var result = new List<Features.Admin.Services.CollectorSummary>();
        int page = 1;
        while (true)
        {
            var paged = await svc.ListCollectorsAsync(null, null, null, page++, 200);
            result.AddRange(paged.Items);
            if (result.Count >= paged.Total) break;
        }
        return result;
    }

    private static string BuildStepLabel(ScenarioStep step, int index) =>
        step.Action == "send-command"
            ? $"Step {index}: {step.CommandType}"
            : $"Step {index}: Power {(step.Power == true ? "on" : "off")}";

    private static object BuildRunEvent(Guid runId, string name, string status, DateTime? endedAt) =>
        new { scenarioRunId = runId, name, status, startedAt = (object?)null, endedAt };

    internal sealed class RunState(Guid runId, string scenarioName, CancellationTokenSource cts)
    {
        internal Guid RunId { get; } = runId;
        internal string ScenarioName { get; } = scenarioName;
        internal CancellationTokenSource Cts { get; } = cts;
    }

    private static readonly Features.Admin.Models.Actor SimActor =
        new("Simulator", "Simulator");
}
