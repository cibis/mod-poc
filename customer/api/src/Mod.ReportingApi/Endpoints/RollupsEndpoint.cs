using Microsoft.EntityFrameworkCore;
using Mod.ReportingApi.Data;

namespace Mod.ReportingApi.Endpoints;

public static class RollupsEndpoint
{
    private const string RangeTooLarge = "RangeTooLarge";

    public static async Task<IResult> HandleSiteSummary(
        Guid siteId,
        string? from,
        string? to,
        ReportingDbContext db,
        CancellationToken ct)
    {
        var range = ParseRange(from, to);
        if (range is null)
            return RangeTooLargeResult();

        var (fromUtc, toUtc) = range.Value;

        // Verify the site exists and belongs to this tenant (query filter handles tenant isolation)
        var siteExists = await db.Sites.AnyAsync(s => s.SiteId == siteId, ct);
        if (!siteExists)
            return Results.NotFound();

        // Aggregate rollups per line for the site
        var lines = await db.Lines
            .Where(l => l.SiteId == siteId)
            .Select(l => new { l.LineId, l.Name })
            .ToListAsync(ct);

        var rollupTotals = await db.RollupMinutes
            .Where(r => r.SiteId == siteId && r.MinuteStart >= fromUtc && r.MinuteStart < toUtc)
            .GroupBy(r => r.LineId)
            .Select(g => new
            {
                LineId = g.Key,
                EventCount = g.Sum(r => r.EventCount),
                CounterDelta = g.Sum(r => r.CounterDelta),
                FaultEventCount = g.Sum(r => r.FaultEventCount),
                RestatedMinutes = g.Count(r => r.IsRestated),
            })
            .ToListAsync(ct);

        var totalsMap = rollupTotals.ToDictionary(r => r.LineId);

        var result = lines.OrderBy(l => l.Name).Select(l =>
        {
            totalsMap.TryGetValue(l.LineId, out var totals);
            return new
            {
                lineId = l.LineId,
                name = l.Name,
                eventCount = totals?.EventCount ?? 0,
                counterDelta = totals?.CounterDelta ?? 0L,
                faultEventCount = totals?.FaultEventCount ?? 0,
                restatedMinutes = totals?.RestatedMinutes ?? 0,
            };
        });

        return Results.Ok(result);
    }

    public static async Task<IResult> HandleAssetRollups(
        Guid assetId,
        string? from,
        string? to,
        ReportingDbContext db,
        CancellationToken ct)
    {
        var range = ParseRange(from, to);
        if (range is null)
            return RangeTooLargeResult();

        var (fromUtc, toUtc) = range.Value;

        // Verify asset exists and belongs to tenant (query filter handles tenant isolation)
        var assetExists = await db.Assets.AnyAsync(a => a.AssetId == assetId, ct);
        if (!assetExists)
            return Results.NotFound();

        var rollups = await db.RollupMinutes
            .Where(r => r.AssetId == assetId && r.MinuteStart >= fromUtc && r.MinuteStart < toUtc)
            .OrderBy(r => r.MinuteStart)
            .Select(r => new
            {
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

        return Results.Ok(rollups);
    }

    private static (DateTime From, DateTime To)? ParseRange(string? from, string? to)
    {
        var now = DateTime.UtcNow;
        var fromUtc = from is not null && DateTime.TryParse(from, null, System.Globalization.DateTimeStyles.RoundtripKind, out var f)
            ? f.ToUniversalTime()
            : now.AddHours(-1);
        var toUtc = to is not null && DateTime.TryParse(to, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.ToUniversalTime()
            : now;

        if ((toUtc - fromUtc).TotalHours > 24)
            return null;

        return (fromUtc, toUtc);
    }

    private static IResult RangeTooLargeResult() =>
        Results.Problem(type: RangeTooLarge, statusCode: StatusCodes.Status400BadRequest);
}
