using Mod.PortalApi.Features.Admin.Models;

namespace Mod.PortalApi.Features.Admin.Services;

internal interface ISiteService
{
    Task<SiteRow> CreateSiteAsync(Guid tenantId, string name, string regionLabel, string timeZone, Actor actor);
    Task<LineRow> CreateLineAsync(Guid siteId, string name, Actor actor);
    Task<AssetRow> CreateAssetAsync(Guid lineId, string name, string assetType,
        string? processValueName, string? processValueUnit,
        double? processValueMin, double? processValueMax, Actor actor);
}

internal sealed record SiteRow(
    Guid SiteId, Guid TenantId, string Name, string RegionLabel, string TimeZone, DateTime CreatedAt);

internal sealed record LineRow(
    Guid LineId, Guid TenantId, Guid SiteId, string Name, DateTime CreatedAt);

internal sealed record AssetRow(
    Guid AssetId, Guid TenantId, Guid SiteId, Guid LineId, string Name,
    string AssetType, string? ProcessValueName, string? ProcessValueUnit,
    double? ProcessValueMin, double? ProcessValueMax, DateTime CreatedAt);
