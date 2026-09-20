using Microsoft.EntityFrameworkCore;
using Mod.ReportingApi.Auth;
using Mod.ReportingApi.Data;

namespace Mod.ReportingApi.Endpoints;

public static class HierarchyEndpoint
{
    public static async Task<IResult> Handle(
        ITenantContext tenant,
        ReportingDbContext db,
        CancellationToken ct)
    {
        var tenantRecord = await db.Tenants
            .Where(t => t.TenantId == tenant.TenantId)
            .Select(t => new { t.TenantId, t.Name })
            .FirstOrDefaultAsync(ct);

        if (tenantRecord is null)
            return Results.NotFound();

        var sites = await db.Sites
            .Include(s => s.Lines)
                .ThenInclude(l => l.Assets)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        var result = new
        {
            tenantId = tenantRecord.TenantId,
            tenantName = tenantRecord.Name,
            sites = sites.Select(s => new
            {
                siteId = s.SiteId,
                name = s.Name,
                regionLabel = s.RegionLabel,
                timeZone = s.TimeZone,
                lines = s.Lines.OrderBy(l => l.Name).Select(l => new
                {
                    lineId = l.LineId,
                    name = l.Name,
                    assets = l.Assets.OrderBy(a => a.Name).Select(a => new
                    {
                        assetId = a.AssetId,
                        name = a.Name,
                        assetType = a.AssetType,
                        counterName = a.CounterName,
                        processValueName = a.ProcessValueName,
                        processValueUnit = a.ProcessValueUnit,
                    }),
                }),
            }),
        };

        return Results.Ok(result);
    }
}
