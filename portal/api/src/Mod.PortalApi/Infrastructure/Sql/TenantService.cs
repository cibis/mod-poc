using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Infrastructure.Sql;

internal sealed class TenantService(SqlConfig sqlConfig) : ITenantService
{
    public async Task<IReadOnlyList<TenantSummary>> ListTenantsAsync()
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        var rows = await conn.QueryAsync<TenantSummary>(
            """
            SELECT
                t.TenantId, t.Name, t.Slug, t.IsolationMode, t.Status,
                COUNT(DISTINCT s.SiteId) AS SiteCount,
                COUNT(DISTINCT c.CollectorId) AS CollectorCount
            FROM registry.Tenant t
            LEFT JOIN registry.Site s ON s.TenantId = t.TenantId
            LEFT JOIN registry.Collector c ON c.TenantId = t.TenantId
            GROUP BY t.TenantId, t.Name, t.Slug, t.IsolationMode, t.Status
            ORDER BY t.Name
            """);
        return rows.AsList();
    }

    public async Task<TenantRow> CreateTenantAsync(string name, string isolationMode, Actor actor)
    {
        if (!string.Equals(isolationMode, "Pooled", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DedicatedNotSupportedInPoc");

        var tenantId = Guid.NewGuid();
        var slug = GenerateSlug(name);

        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.ExecuteAsync(
            """
            INSERT INTO registry.Tenant (TenantId, Name, Slug, IsolationMode, Status, CreatedAt)
            VALUES (@TenantId, @Name, @Slug, 'Pooled', 'Active', GETUTCDATE())
            """,
            new { TenantId = tenantId, Name = name, Slug = slug });

        return new TenantRow(tenantId, name, slug, "Pooled", "Active", DateTime.UtcNow);
    }

    public async Task<TenantHierarchy?> GetHierarchyAsync(Guid tenantId)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        var tenant = await conn.QuerySingleOrDefaultAsync<TenantRow>(
            "SELECT TenantId, Name, Slug, IsolationMode, Status, CreatedAt FROM registry.Tenant WHERE TenantId = @TenantId",
            new { TenantId = tenantId });

        if (tenant is null) return null;

        var sites = (await conn.QueryAsync<SiteRow>(
            "SELECT SiteId, TenantId, Name, RegionLabel, TimeZone, CreatedAt FROM registry.Site WHERE TenantId = @TenantId ORDER BY CreatedAt",
            new { TenantId = tenantId })).AsList();

        var lines = (await conn.QueryAsync<(Guid LineId, Guid SiteId, string Name, DateTime CreatedAt)>(
            "SELECT LineId, SiteId, Name, CreatedAt FROM registry.Line WHERE TenantId = @TenantId ORDER BY SiteId, CreatedAt",
            new { TenantId = tenantId })).AsList();

        var assets = (await conn.QueryAsync<(Guid AssetId, Guid LineId, string Name, string AssetType,
                string? ProcessValueName, string? ProcessValueUnit, double? ProcessValueMin, double? ProcessValueMax)>(
            """
            SELECT AssetId, LineId, Name, AssetType,
                   ProcessValueName, ProcessValueUnit, ProcessValueMin, ProcessValueMax
            FROM registry.Asset WHERE TenantId = @TenantId ORDER BY LineId, CreatedAt
            """,
            new { TenantId = tenantId })).AsList();

        var collectors = (await conn.QueryAsync<CollectorSummaryItem>(
            """
            SELECT CollectorId, Name, Status, CommandChannelEnabled, LastSeenAt, SoftwareVersion
            FROM registry.Collector WHERE TenantId = @TenantId ORDER BY SiteId, CreatedAt
            """,
            new { TenantId = tenantId })).AsList();

        var assetsBySite = assets
            .Join(lines, a => a.LineId, l => l.LineId, (a, l) => (a, l.SiteId))
            .GroupBy(x => x.SiteId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.a).ToList());

        var linesBySite = lines.GroupBy(l => l.SiteId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var collectorsBySite = collectors.GroupBy(c => c.CollectorId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Rebuild per-site
        var collectorsBySiteId = (await conn.QueryAsync<(Guid CollectorId, Guid SiteId, string Name, string Status,
                bool CommandChannelEnabled, DateTime? LastSeenAt, string? SoftwareVersion)>(
            """
            SELECT CollectorId, SiteId, Name, Status, CommandChannelEnabled, LastSeenAt, SoftwareVersion
            FROM registry.Collector WHERE TenantId = @TenantId ORDER BY SiteId, CreatedAt
            """,
            new { TenantId = tenantId }))
            .GroupBy(c => c.SiteId)
            .ToDictionary(g => g.Key, g => g.Select(c => new CollectorSummaryItem(
                c.CollectorId, c.Name, c.Status, c.CommandChannelEnabled, c.LastSeenAt, c.SoftwareVersion)).ToList());

        var assetsByLine = assets.GroupBy(a => a.LineId)
            .ToDictionary(g => g.Key, g => g.Select(a => new AssetItem(
                a.AssetId, a.Name, a.AssetType, a.ProcessValueName, a.ProcessValueUnit,
                a.ProcessValueMin, a.ProcessValueMax)).ToList());

        var siteHierarchies = sites.Select(s =>
        {
            var siteLines = linesBySite.TryGetValue(s.SiteId, out var sl) ? sl : [];
            var linesWithAssets = siteLines.Select(l =>
            {
                var la = assetsByLine.TryGetValue(l.LineId, out var la2) ? la2 : [];
                return new LineWithAssets(l.LineId, l.Name, l.CreatedAt, la);
            }).ToList();

            var siteCollectors = collectorsBySiteId.TryGetValue(s.SiteId, out var sc) ? sc : [];

            return new SiteWithHierarchy(
                s.SiteId, s.Name, s.RegionLabel, s.TimeZone, s.CreatedAt,
                linesWithAssets,
                siteCollectors);
        }).ToList();

        return new TenantHierarchy(tenant, siteHierarchies);
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9]+", "-");
        slug = slug.Trim('-');
        if (slug.Length > 50) slug = slug[..50].TrimEnd('-');
        return slug;
    }
}
