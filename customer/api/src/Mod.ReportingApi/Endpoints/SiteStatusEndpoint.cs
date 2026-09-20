using Microsoft.EntityFrameworkCore;
using Mod.ReportingApi.Data;

namespace Mod.ReportingApi.Endpoints;

public static class SiteStatusEndpoint
{
    public static async Task<IResult> Handle(
        ReportingDbContext db,
        IConfiguration config,
        CancellationToken ct)
    {
        var staleSeconds = int.TryParse(config["FRESHNESS_STALE_SECONDS"], out var s) ? s : 60;
        var now = DateTime.UtcNow;
        var hourAgo = now.AddHours(-1);
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

        // Freshness: max LastEventTime per site, derived from CollectorFreshness (no access to registry.Collector)
        var freshnessBySite = await db.CollectorFreshnesses
            .GroupBy(cf => cf.SiteId)
            .Select(g => new
            {
                SiteId = g.Key,
                LastEventTime = g.Max(cf => cf.LastEventTime),
            })
            .ToListAsync(ct);

        // Completeness: sum over collectors whose TenantId matches, for completed minutes in last hour
        var reconciliationBySite = await (
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

        // Open gap count per site (join SequenceGap → CollectorFreshness for SiteId)
        var openGapsBySite = await (
            from gap in db.SequenceGaps
            join cf in db.CollectorFreshnesses on gap.CollectorId equals cf.CollectorId
            where gap.FilledAt == null
            group gap by cf.SiteId into g
            select new { SiteId = g.Key, Count = g.Count() }
        ).ToListAsync(ct);

        // All sites for this tenant
        var sites = await db.Sites
            .OrderBy(s => s.Name)
            .Select(s => new { s.SiteId, s.Name })
            .ToListAsync(ct);

        var freshMap = freshnessBySite.ToDictionary(f => f.SiteId);
        var reconMap = reconciliationBySite.ToDictionary(r => r.SiteId);
        var gapMap = openGapsBySite.ToDictionary(g => g.SiteId);

        var result = sites.Select(site =>
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
        });

        return Results.Ok(result);
    }
}
