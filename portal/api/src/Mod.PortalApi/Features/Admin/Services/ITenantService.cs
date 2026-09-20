using Mod.PortalApi.Features.Admin.Models;

namespace Mod.PortalApi.Features.Admin.Services;

internal interface ITenantService
{
    Task<IReadOnlyList<TenantSummary>> ListTenantsAsync();
    Task<TenantRow> CreateTenantAsync(string name, string isolationMode, Actor actor);
    Task<TenantHierarchy?> GetHierarchyAsync(Guid tenantId);
}

internal sealed record TenantSummary(
    Guid TenantId, string Name, string Slug, string IsolationMode, string Status,
    int SiteCount, int CollectorCount);

internal sealed record TenantRow(
    Guid TenantId, string Name, string Slug, string IsolationMode, string Status, DateTime CreatedAt);

internal sealed record TenantHierarchy(TenantRow Tenant, IReadOnlyList<SiteWithHierarchy> Sites);

internal sealed record SiteWithHierarchy(
    Guid SiteId, string Name, string RegionLabel, string TimeZone, DateTime CreatedAt,
    IReadOnlyList<LineWithAssets> Lines,
    IReadOnlyList<CollectorSummaryItem> Collectors);

internal sealed record LineWithAssets(
    Guid LineId, string Name, DateTime CreatedAt,
    IReadOnlyList<AssetItem> Assets);

internal sealed record AssetItem(
    Guid AssetId, string Name, string AssetType, string? ProcessValueName,
    string? ProcessValueUnit, double? ProcessValueMin, double? ProcessValueMax);

internal sealed record CollectorSummaryItem(
    Guid CollectorId, string Name, string Status, bool CommandChannelEnabled,
    DateTime? LastSeenAt, string? SoftwareVersion);
