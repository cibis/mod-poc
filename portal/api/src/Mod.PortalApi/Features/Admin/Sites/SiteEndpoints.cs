using System.Security.Claims;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Features.Admin.Sites;

internal static class SiteEndpoints
{
    internal static IEndpointRouteBuilder MapSites(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tenants/{tenantId:guid}/sites", CreateSite);
        app.MapPost("/sites/{siteId:guid}/lines", CreateLine);
        app.MapPost("/lines/{lineId:guid}/assets", CreateAsset);
        return app;
    }

    private static async Task<IResult> CreateSite(
        Guid tenantId, CreateSiteRequest req,
        ISiteService svc, IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        var site = await svc.CreateSiteAsync(tenantId, req.Name, req.RegionLabel, req.TimeZone, actor);
        await audit.WriteAsync(actor, "SiteCreated", "Site", site.SiteId.ToString(),
            tenantId, $"{{\"name\":\"{site.Name}\"}}");
        return Results.Created($"/api/admin/sites/{site.SiteId}", site);
    }

    private static async Task<IResult> CreateLine(
        Guid siteId, CreateLineRequest req,
        ISiteService svc, IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        var line = await svc.CreateLineAsync(siteId, req.Name, actor);
        await audit.WriteAsync(actor, "LineCreated", "Line", line.LineId.ToString(),
            line.TenantId, $"{{\"name\":\"{line.Name}\"}}");
        return Results.Created($"/api/admin/lines/{line.LineId}", line);
    }

    private static async Task<IResult> CreateAsset(
        Guid lineId, CreateAssetRequest req,
        ISiteService svc, IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        var asset = await svc.CreateAssetAsync(lineId, req.Name, req.AssetType,
            req.ProcessValueName, req.ProcessValueUnit,
            req.ProcessValueMin, req.ProcessValueMax, actor);
        await audit.WriteAsync(actor, "AssetCreated", "Asset", asset.AssetId.ToString(),
            asset.TenantId, $"{{\"name\":\"{asset.Name}\"}}");
        return Results.Created($"/api/admin/assets/{asset.AssetId}", asset);
    }

    private static Actor GetActor(ClaimsPrincipal user) =>
        new(user.FindFirstValue("name") ?? "Unknown", user.FindFirstValue("kind") ?? "ModAdmin");

    private sealed record CreateSiteRequest(string Name, string RegionLabel, string TimeZone);
    private sealed record CreateLineRequest(string Name);
    private sealed record CreateAssetRequest(
        string Name, string AssetType,
        string? ProcessValueName, string? ProcessValueUnit,
        double? ProcessValueMin, double? ProcessValueMax);
}
