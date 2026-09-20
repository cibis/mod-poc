// DEMO SCAFFOLDING
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;
using Mod.PortalApi.Features.Simulation.Channel;
using Mod.PortalApi.Features.Simulation.Data;
using Mod.PortalApi.Features.Simulation.Models;
using Mod.PortalApi.Features.Simulation.Provisioning;
using Mod.PortalApi.Features.Simulation.Scenarios;
using Mod.PortalApi.Features.Simulation.Telemetry;

namespace Mod.PortalApi.Features.Simulation.Endpoints;

internal static class SimEndpoints
{
    internal static IEndpointRouteBuilder MapSim(this IEndpointRouteBuilder app)
    {
        var sim = app.MapGroup("/api/sim")
            .RequireAuthorization("AdminOnly")
            .AddEndpointFilter(async (ctx, next) =>
            {
                ctx.HttpContext.Response.Headers.TryAdd("X-Mod-Demo-Scaffolding", "true");
                return await next(ctx);
            });

        sim.MapGet("/collectors", GetCollectorsAsync);
        sim.MapPost("/power", PowerAsync);
        sim.MapPost("/commands", SendCommandsAsync);
        sim.MapPost("/fleet/generate", GenerateFleetAsync);
        sim.MapGet("/scenarios", GetScenarios);
        sim.MapPost("/scenarios/{name}/run", RunScenarioAsync);
        sim.MapPost("/scenario-runs/{id}/stop", StopScenarioRunAsync);
        sim.MapGet("/scenario-runs", GetScenarioRunsAsync);
        sim.MapGet("/topology", GetTopologyAsync);
        sim.MapGet("/metrics", GetMetricsAsync);
        sim.MapGet("/timeline", GetTimelineAsync);

        return app;
    }

    // GET /api/sim/collectors
    private static async Task<IResult> GetCollectorsAsync(
        ICollectorService collectorSvc,
        SimRepository repo,
        SimStateCache stateCache)
    {
        var allCollectors = await GetAllCollectorsAsync(collectorSvc);
        var states = (await repo.GetAllCollectorStatesAsync())
            .ToDictionary(s => s.CollectorId);

        var result = allCollectors.Select(c =>
        {
            states.TryGetValue(c.CollectorId, out var state);
            var latestStatus = stateCache.GetStatus(c.CollectorId);
            var lastStatusAt = stateCache.GetLastStatusAt(c.CollectorId);

            return new
            {
                collectorId = c.CollectorId,
                name = c.Name,
                tenantId = c.TenantId,
                tenantName = c.TenantName,
                siteId = c.SiteId,
                siteName = c.SiteName,
                regionLabel = c.RegionLabel,
                status = c.Status,
                poweredOn = state?.PoweredOn ?? false,
                provisioningState = state?.ProvisioningState ?? "Off",
                lastError = state?.LastError,
                bufferOverflowEver = state?.BufferOverflowEver ?? false,
                simStatus = latestStatus,
                lastStatusAt,
            };
        });

        return Results.Ok(result);
    }

    // POST /api/sim/power — { targets, on }
    private static async Task<IResult> PowerAsync(
        PowerRequest req,
        ICollectorService collectorSvc,
        PowerOperations power,
        SimStateCache stateCache)
    {
        var targets = await ResolveTargetsAsync(req.Targets, collectorSvc, stateCache);
        var accepted = new List<Guid>();
        var skipped = new List<object>();

        var actor = new Actor("Simulator", "Simulator"); // actual actor resolved per endpoint via claims; set here for batch

        foreach (var collectorId in targets)
        {
            if (req.On)
            {
                var result = await power.PowerOnAsync(collectorId, actor);
                if (result.Success) accepted.Add(collectorId);
                else skipped.Add(new { collectorId, reason = result.Reason });
            }
            else
            {
                await power.PowerOffAsync(collectorId, actor);
                accepted.Add(collectorId);
            }
        }

        return Results.Ok(new { accepted, skipped });
    }

    // POST /api/sim/commands — { targets, command:{type, args} }
    private static async Task<IResult> SendCommandsAsync(
        CommandRequest req,
        ICollectorService collectorSvc,
        SimStateCache stateCache,
        SimServiceBusClient? sbClient,
        SimRepository repo)
    {
        if (sbClient is null)
            return Results.Problem(type: "SimulatorNotConfigured", statusCode: 503,
                title: "Simulator Service Bus not configured");

        // Commands go to powered-on collectors only.
        var all = await GetAllCollectorsAsync(collectorSvc);
        var targets = ResolveTargets(req.Targets, all)
            .Where(id => stateCache.GetProvisioningState(id) is "Running" or "Provisioning")
            .ToList();

        var commandId = Guid.NewGuid();
        var sentTo = new List<Guid>();

        foreach (var collectorId in targets)
        {
            var cmd = new SimCommandBody(commandId, req.Command.Type, DateTime.UtcNow,
                req.Command.Args);
            try
            {
                await sbClient.SendCommandAsync(collectorId, cmd);
                sentTo.Add(collectorId);
            }
            catch { /* log but continue */ }
        }

        // Write Command timeline marker.
        await repo.InsertTimelineMarkerAsync("Command",
            $"{req.Command.Type} — {targets.Count} collector(s)", targets, null);

        return Results.Ok(new { commandId, sentTo });
    }

    // POST /api/sim/fleet/generate
    private static async Task<IResult> GenerateFleetAsync(
        FleetGenerateRequest req,
        ITenantService tenantSvc,
        ISiteService siteSvc,
        ICollectorService collectorSvc,
        HttpContext ctx)
    {
        var actor = ExtractActor(ctx);
        var createdCollectorIds = new List<Guid>();

        string[] assetTypes = ["Filler", "Capper", "Labeler", "Packer"];

        for (int si = 0; si < req.Sites; si++)
        {
            var siteRow = await siteSvc.CreateSiteAsync(
                req.TenantId,
                $"Generated Site {si + 1}",
                req.RegionLabel,
                "Europe/Berlin",
                actor);

            for (int li = 0; li < req.LinesPerSite; li++)
            {
                var lineRow = await siteSvc.CreateLineAsync(
                    siteRow.SiteId, $"Line {li + 1}", actor);

                for (int ai = 0; ai < req.AssetsPerLine; ai++)
                {
                    var assetType = assetTypes[ai % assetTypes.Length];
                    await siteSvc.CreateAssetAsync(
                        lineRow.LineId,
                        $"{lineRow.Name} {assetType}",
                        assetType,
                        "speed", "units/min", 0, 600,
                        actor);
                }
            }

            var collectorRow = await collectorSvc.RegisterCollectorAsync(
                siteRow.SiteId,
                $"Generated Site {si + 1} Collector",
                req.CommandChannelEnabled,
                actor);

            createdCollectorIds.Add(collectorRow.CollectorId);
        }

        return Results.Ok(new { collectorIds = createdCollectorIds });
    }

    // GET /api/sim/scenarios
    private static IResult GetScenarios()
    {
        var result = ScenarioLoader.All.Select(s => new
        {
            name = s.Name,
            description = s.Description,
            durationSeconds = s.DurationSeconds,
            parameters = s.Parameters.Select(p => new
            {
                name = p.Name,
                type = p.Type,
                @default = p.Default,
            }),
        });
        return Results.Ok(result);
    }

    // POST /api/sim/scenarios/{name}/run
    private static async Task<IResult> RunScenarioAsync(
        string name,
        [FromBody] RunScenarioRequest req,
        ScenarioRunner runner,
        SimRepository repo,
        HttpContext ctx)
    {
        if (await repo.HasActiveScenarioRunAsync())
            return Results.Conflict(new { error = "A scenario run is already active" });

        var actor = ExtractActor(ctx);
        var parameters = req.Parameters ?? new Dictionary<string, object?>();
        var runId = await runner.StartAsync(name, parameters, actor.Name);
        if (runId is null)
            return Results.Conflict(new { error = "A scenario run is already active" });

        return Results.Ok(new { scenarioRunId = runId });
    }

    // POST /api/sim/scenario-runs/{id}/stop
    private static async Task<IResult> StopScenarioRunAsync(
        Guid id,
        ScenarioRunner runner)
    {
        await runner.StopAsync(id);
        return Results.NoContent();
    }

    // GET /api/sim/scenario-runs?limit
    private static async Task<IResult> GetScenarioRunsAsync(
        SimRepository repo,
        int limit = 20)
    {
        var runs = await repo.GetRecentScenarioRunsAsync(Math.Clamp(limit, 1, 100));
        return Results.Ok(runs);
    }

    // GET /api/sim/topology
    private static async Task<IResult> GetTopologyAsync(
        SimRepository repo,
        SimStateCache stateCache,
        ContainerAppProvisioner? provisioner)
    {
        var states = await repo.GetAllCollectorStatesAsync();
        var statuses = stateCache.GetAllStatuses();

        // Best-effort replica counts.
        var replicaCounts = new Dictionary<string, int>();
        if (provisioner is not null)
        {
            string[] apps = ["ca-ingest", "ca-processing", "ca-mgmt", "ca-portal", "ca-reporting"];
            string[] keys = ["ingest", "processing", "management", "portal", "reporting"];
            for (int i = 0; i < apps.Length; i++)
            {
                replicaCounts[keys[i]] = await provisioner.GetRunningReplicaCountAsync(apps[i]);
            }
        }

        var topology = TopologyBuilder.Build(states, statuses, replicaCounts);
        return Results.Ok(topology);
    }

    // GET /api/sim/metrics?keys&from&to
    private static async Task<IResult> GetMetricsAsync(
        SimRepository repo,
        [FromQuery] string[]? keys,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        if (keys is null || keys.Length == 0)
            return Results.BadRequest(new { error = "keys parameter required" });

        var fromDt = from ?? DateTime.UtcNow.AddHours(-1);
        var toDt = to ?? DateTime.UtcNow;

        var rows = await repo.QueryMetricSamplesAsync(keys, fromDt, toDt);

        var series = rows
            .GroupBy(r => r.Key)
            .Select(g => new
            {
                key = g.Key,
                points = g.Select(r => new[] { (double)r.AtMs, r.Value }).ToList(),
            });

        return Results.Ok(new { series });
    }

    // GET /api/sim/timeline?from&to
    private static async Task<IResult> GetTimelineAsync(
        SimRepository repo,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var fromDt = from ?? DateTime.UtcNow.AddHours(-1);
        var toDt = to ?? DateTime.UtcNow;
        var markers = await repo.QueryTimelineAsync(fromDt, toDt);
        return Results.Ok(markers);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<CollectorSummary>> GetAllCollectorsAsync(
        ICollectorService svc)
    {
        var result = new List<CollectorSummary>();
        int page = 1;
        while (true)
        {
            var paged = await svc.ListCollectorsAsync(null, null, null, page++, 200);
            result.AddRange(paged.Items);
            if (result.Count >= paged.Total) break;
        }
        return result;
    }

    private static async Task<IReadOnlyList<Guid>> ResolveTargetsAsync(
        TargetSelector? selector,
        ICollectorService collectorSvc,
        SimStateCache stateCache)
    {
        var all = await GetAllCollectorsAsync(collectorSvc);
        return ResolveTargets(selector, all);
    }

    private static IReadOnlyList<Guid> ResolveTargets(
        TargetSelector? selector,
        IReadOnlyList<CollectorSummary> all)
    {
        if (selector is null) return [];

        IEnumerable<CollectorSummary> filtered = all;

        if (selector.CollectorIds is { Count: > 0 })
            filtered = filtered.Where(c => selector.CollectorIds.Contains(c.CollectorId));
        else if (selector.All == true) { /* no filter */ }
        else
        {
            if (selector.TenantId.HasValue)
                filtered = filtered.Where(c => c.TenantId == selector.TenantId.Value);
            if (selector.SiteIds is { Count: > 0 })
                filtered = filtered.Where(c => selector.SiteIds.Contains(c.SiteId));
            if (selector.RegionLabel is not null)
                filtered = filtered.Where(c => c.RegionLabel == selector.RegionLabel);
        }

        return filtered.Select(c => c.CollectorId).Distinct().ToList();
    }

    private static Actor ExtractActor(HttpContext ctx)
    {
        var name = ctx.User.FindFirst("name")?.Value
                   ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                   ?? "Admin";
        var kind = ctx.User.FindFirst("kind")?.Value ?? "ModAdmin";
        return new Actor(name, kind);
    }

    // ── Request / response models ────────────────────────────────────────────

    private sealed record PowerRequest(TargetSelector? Targets, bool On);

    private sealed record CommandRequest(
        TargetSelector? Targets,
        CommandSpec Command);

    private sealed record CommandSpec(
        string Type,
        IReadOnlyDictionary<string, object?>? Args);

    private sealed record FleetGenerateRequest(
        Guid TenantId,
        int Sites,
        int LinesPerSite,
        int AssetsPerLine,
        string RegionLabel,
        bool CommandChannelEnabled);

    private sealed record RunScenarioRequest(
        Dictionary<string, object?>? Parameters);
}
