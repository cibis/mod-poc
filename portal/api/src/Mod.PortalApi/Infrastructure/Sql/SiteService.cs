using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Infrastructure.Sql;

internal sealed class SiteService(SqlConfig sqlConfig) : ISiteService
{
    public async Task<SiteRow> CreateSiteAsync(Guid tenantId, string name, string regionLabel,
        string timeZone, Actor actor)
    {
        var siteId = Guid.NewGuid();
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.ExecuteAsync(
            """
            INSERT INTO registry.Site (SiteId, TenantId, Name, RegionLabel, TimeZone, CreatedAt)
            VALUES (@SiteId, @TenantId, @Name, @RegionLabel, @TimeZone, GETUTCDATE())
            """,
            new { SiteId = siteId, TenantId = tenantId, Name = name, RegionLabel = regionLabel, TimeZone = timeZone });

        return new SiteRow(siteId, tenantId, name, regionLabel, timeZone, DateTime.UtcNow);
    }

    public async Task<LineRow> CreateLineAsync(Guid siteId, string name, Actor actor)
    {
        var lineId = Guid.NewGuid();
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        var tenantId = await conn.QuerySingleAsync<Guid>(
            "SELECT TenantId FROM registry.Site WHERE SiteId = @SiteId", new { SiteId = siteId });

        await conn.ExecuteAsync(
            """
            INSERT INTO registry.Line (LineId, TenantId, SiteId, Name, CreatedAt)
            VALUES (@LineId, @TenantId, @SiteId, @Name, GETUTCDATE())
            """,
            new { LineId = lineId, TenantId = tenantId, SiteId = siteId, Name = name });

        return new LineRow(lineId, tenantId, siteId, name, DateTime.UtcNow);
    }

    public async Task<AssetRow> CreateAssetAsync(Guid lineId, string name, string assetType,
        string? processValueName, string? processValueUnit,
        double? processValueMin, double? processValueMax, Actor actor)
    {
        var assetId = Guid.NewGuid();
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        var (tenantId, siteId) = await conn.QuerySingleAsync<(Guid TenantId, Guid SiteId)>(
            "SELECT TenantId, SiteId FROM registry.Line WHERE LineId = @LineId",
            new { LineId = lineId });

        await conn.ExecuteAsync(
            """
            INSERT INTO registry.Asset
                (AssetId, TenantId, SiteId, LineId, Name, AssetType,
                 ProcessValueName, ProcessValueUnit, ProcessValueMin, ProcessValueMax, CreatedAt)
            VALUES
                (@AssetId, @TenantId, @SiteId, @LineId, @Name, @AssetType,
                 @ProcessValueName, @ProcessValueUnit, @ProcessValueMin, @ProcessValueMax, GETUTCDATE())
            """,
            new
            {
                AssetId = assetId, TenantId = tenantId, SiteId = siteId, LineId = lineId,
                Name = name, AssetType = assetType,
                ProcessValueName = processValueName, ProcessValueUnit = processValueUnit,
                ProcessValueMin = processValueMin, ProcessValueMax = processValueMax,
            });

        return new AssetRow(assetId, tenantId, siteId, lineId, name, assetType,
            processValueName, processValueUnit, processValueMin, processValueMax, DateTime.UtcNow);
    }
}
