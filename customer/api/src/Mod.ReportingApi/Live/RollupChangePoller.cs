using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Mod.ReportingApi.Auth;
using Mod.ReportingApi.Data;

namespace Mod.ReportingApi.Live;

public sealed class RollupChangePoller(
    ConnectedTenantTracker tracker,
    IHubContext<LiveHub> hub,
    IServiceScopeFactory scopeFactory,
    IConfiguration config,
    ILogger<RollupChangePoller> logger) : BackgroundService
{
    // Watermark per tenant: only rollups computed after this timestamp are pushed
    private readonly ConcurrentDictionary<Guid, DateTime> _watermarks = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var pollSeconds = int.TryParse(config["LIVE_POLL_SECONDS"], out var p) ? p : 2;
        var rollupTask = RunRollupLoop(TimeSpan.FromSeconds(pollSeconds), ct);
        var freshnessTask = RunFreshnessLoop(TimeSpan.FromSeconds(5), ct);
        await Task.WhenAll(rollupTask, freshnessTask);
    }

    private async Task RunRollupLoop(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            foreach (var tenantId in tracker.ActiveTenants)
            {
                try { await PushRollupChanges(tenantId, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogError(ex, "Error pushing rollup changes for tenant {TenantId}", tenantId); }
            }
        }
    }

    private async Task RunFreshnessLoop(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            foreach (var tenantId in tracker.ActiveTenants)
            {
                try { await PushFreshnessUpdate(tenantId, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { logger.LogError(ex, "Error pushing freshness update for tenant {TenantId}", tenantId); }
            }
        }
    }

    private async Task PushRollupChanges(Guid tenantId, CancellationToken ct)
    {
        var watermark = _watermarks.GetOrAdd(tenantId, _ => DateTime.UtcNow);

        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantCtx = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantCtx.TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

        var newRollups = await db.RollupMinutes
            .Where(r => r.LastComputedAt > watermark)
            .OrderBy(r => r.MinuteStart)
            .Select(r => new
            {
                assetId = r.AssetId,
                minuteStart = r.MinuteStart.ToString("O"),
                eventCount = r.EventCount,
                counterDelta = r.CounterDelta,
                faultEventCount = r.FaultEventCount,
                lastState = r.LastState,
                processValueAvg = r.ProcessValueAvg,
                processValueMin = r.ProcessValueMin,
                processValueMax = r.ProcessValueMax,
                isRestated = r.IsRestated,
                restatementCount = r.RestatementCount,
                lastComputedAt = r.LastComputedAt.ToString("O"),
            })
            .ToListAsync(ct);

        if (newRollups.Count == 0)
            return;

        var maxComputedAt = await db.RollupMinutes
            .Where(r => r.LastComputedAt > watermark)
            .MaxAsync(r => r.LastComputedAt, ct);

        _watermarks[tenantId] = maxComputedAt;

        await hub.Clients.Group($"tenant:{tenantId}")
            .SendAsync("rollupsUpdated", new { items = newRollups }, ct);
    }

    private async Task PushFreshnessUpdate(Guid tenantId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantCtx = scope.ServiceProvider.GetRequiredService<TenantContext>();
        tenantCtx.TenantId = tenantId;
        var db = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

        var staleSeconds = int.TryParse(config["FRESHNESS_STALE_SECONDS"], out var s) ? s : 60;
        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

        var freshnessBySite = await db.CollectorFreshnesses
            .GroupBy(cf => cf.SiteId)
            .Select(g => new { SiteId = g.Key, LastEventTime = g.Max(cf => cf.LastEventTime) })
            .ToListAsync(ct);

        var reconBySite = await (
            from r in db.Reconciliations
            join cf in db.CollectorFreshnesses on r.CollectorId equals cf.CollectorId
            where r.MinuteStart >= hourAgo && r.MinuteStart < currentMinute
            group new { r, cf } by cf.SiteId into g
            select new
            {
                SiteId = g.Key,
                Produced = g.Sum(x => (int?)x.r.ProducedCount) ?? 0,
                Accepted = g.Sum(x => x.r.AcceptedCount),
                DeadLettered = g.Sum(x => x.r.DeadLetteredCount),
            }
        ).ToListAsync(ct);

        var gapsBySite = await (
            from gap in db.SequenceGaps
            join cf in db.CollectorFreshnesses on gap.CollectorId equals cf.CollectorId
            where gap.FilledAt == null
            group gap by cf.SiteId into g
            select new { SiteId = g.Key, Count = g.Count() }
        ).ToListAsync(ct);

        var sites = await db.Sites
            .OrderBy(s => s.Name)
            .Select(s => new { s.SiteId, s.Name })
            .ToListAsync(ct);

        var freshMap = freshnessBySite.ToDictionary(f => f.SiteId);
        var reconMap = reconBySite.ToDictionary(r => r.SiteId);
        var gapMap = gapsBySite.ToDictionary(g => g.SiteId);

        var siteStatuses = sites.Select(site =>
        {
            freshMap.TryGetValue(site.SiteId, out var fresh);
            reconMap.TryGetValue(site.SiteId, out var recon);
            gapMap.TryGetValue(site.SiteId, out var gaps);

            string status;
            double ageSeconds = 0;
            DateTime? lastEventTime = null;

            if (fresh is null)
            {
                status = "NoData";
            }
            else
            {
                lastEventTime = fresh.LastEventTime;
                ageSeconds = (now - fresh.LastEventTime).TotalSeconds;
                status = ageSeconds <= staleSeconds ? "Current" : "Stale";
            }

            double? completenessRatio = null;
            if (recon is not null && recon.Produced > 0)
                completenessRatio = (double)(recon.Accepted + recon.DeadLettered) / recon.Produced;

            return new
            {
                siteId = site.SiteId,
                name = site.Name,
                freshness = new
                {
                    lastEventTime = lastEventTime?.ToString("O"),
                    ageSeconds = fresh is null ? (double?)null : ageSeconds,
                    status,
                },
                completenessLastHour = new
                {
                    produced = recon?.Produced ?? 0,
                    accepted = recon?.Accepted ?? 0,
                    deadLettered = recon?.DeadLettered ?? 0,
                    ratio = completenessRatio,
                },
                openGapCount = gaps?.Count ?? 0,
            };
        }).ToList();

        await hub.Clients.Group($"tenant:{tenantId}")
            .SendAsync("freshnessUpdated", new { sites = siteStatuses }, ct);
    }
}
