using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mod.ReportingApi.Auth;
using Mod.ReportingApi.Data;

namespace Mod.ReportingApi.Endpoints;

public static class MeEndpoint
{
    public static async Task<IResult> Handle(
        ClaimsPrincipal user,
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

        return Results.Ok(new
        {
            userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            displayName = user.FindFirstValue("name"),
            tenantId = tenant.TenantId,
            tenantName = tenantRecord.Name,
        });
    }
}
