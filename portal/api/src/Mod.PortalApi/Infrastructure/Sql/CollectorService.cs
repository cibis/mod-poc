using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;
using Mod.PortalApi.Infrastructure.ServiceBus;

namespace Mod.PortalApi.Infrastructure.Sql;

internal sealed class CollectorService(
    SqlConfig sqlConfig,
    IQueueManager queueManager,
    IAuditWriter auditWriter) : ICollectorService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<PagedResult<CollectorSummary>> ListCollectorsAsync(
        Guid? tenantId, Guid? siteId, string? status, int page, int pageSize)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        const string where = """
            WHERE (@TenantId IS NULL OR c.TenantId = @TenantId)
              AND (@SiteId IS NULL OR c.SiteId = @SiteId)
              AND (@Status IS NULL OR c.Status = @Status)
            """;

        var total = await conn.QuerySingleAsync<int>(
            $"SELECT COUNT(*) FROM registry.Collector c {where}",
            new { TenantId = tenantId, SiteId = siteId, Status = status });

        var offset = (page - 1) * pageSize;
        var items = (await conn.QueryAsync<CollectorSummary>(
            $"""
            SELECT
                c.CollectorId, c.Name,
                c.TenantId, t.Name AS TenantName,
                c.SiteId, s.Name AS SiteName, s.RegionLabel,
                c.Status, c.LastSeenAt, c.SoftwareVersion,
                c.ReportedConfigVersion,
                cc.Version AS ConfigVersion,
                cert.ExpiresAt AS CertificateExpiresAt,
                ch.BufferDepthEvents, ch.BufferCapacityEvents,
                DATEDIFF(SECOND, cf.LastEventTime, GETUTCDATE()) AS FreshnessAgeSeconds
            FROM registry.Collector c
            JOIN registry.Tenant t ON t.TenantId = c.TenantId
            JOIN registry.Site s ON s.SiteId = c.SiteId
            LEFT JOIN registry.CollectorConfig cc ON cc.CollectorId = c.CollectorId
            LEFT JOIN registry.CollectorHealth ch ON ch.CollectorId = c.CollectorId
            LEFT JOIN (
                SELECT CollectorId, MAX(ExpiresAt) AS ExpiresAt
                FROM registry.Certificate
                WHERE RevokedAt IS NULL AND SupersededAt IS NULL
                GROUP BY CollectorId
            ) cert ON cert.CollectorId = c.CollectorId
            LEFT JOIN telemetry.CollectorFreshness cf ON cf.CollectorId = c.CollectorId
            {where}
            ORDER BY t.Name, s.Name, c.Name
            OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY
            """,
            new { TenantId = tenantId, SiteId = siteId, Status = status }))
            .AsList();

        return new PagedResult<CollectorSummary>(items, total);
    }

    public async Task<CollectorRow> RegisterCollectorAsync(
        Guid siteId, string name, bool commandChannelEnabled, Actor actor)
    {
        var collectorId = Guid.NewGuid();
        var defaultSettings = JsonSerializer.Serialize(ConfigDocument.Default(), JsonOpts);

        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();

        var tenantId = await conn.QuerySingleAsync<Guid>(
            "SELECT TenantId FROM registry.Site WHERE SiteId = @SiteId",
            new { SiteId = siteId }, tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO registry.Collector
                (CollectorId, TenantId, SiteId, Name, Status, CommandChannelEnabled, CreatedAt)
            VALUES
                (@CollectorId, @TenantId, @SiteId, @Name, 'Registered', @CommandChannelEnabled, GETUTCDATE())
            """,
            new { CollectorId = collectorId, TenantId = tenantId, SiteId = siteId,
                  Name = name, CommandChannelEnabled = commandChannelEnabled }, tx);

        await conn.ExecuteAsync(
            """
            INSERT INTO registry.CollectorConfig (CollectorId, Version, SettingsJson, UpdatedAt, UpdatedBy)
            VALUES (@CollectorId, 1, @SettingsJson, GETUTCDATE(), @UpdatedBy)
            """,
            new { CollectorId = collectorId, SettingsJson = defaultSettings, UpdatedBy = actor.Name }, tx);

        // Get assets ordered by line creation order, then asset creation order within line
        var assets = (await conn.QueryAsync<(Guid AssetId, int LineIdx, int AssetIdx)>(
            """
            SELECT
                a.AssetId,
                CAST(ROW_NUMBER() OVER (ORDER BY l.CreatedAt) AS int) AS LineIdx,
                CAST(ROW_NUMBER() OVER (PARTITION BY l.LineId ORDER BY a.CreatedAt) AS int) AS AssetIdx
            FROM registry.Asset a
            JOIN registry.Line l ON l.LineId = a.LineId
            WHERE l.SiteId = @SiteId
            """,
            new { SiteId = siteId }, tx)).AsList();

        foreach (var asset in assets)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO registry.AssetSourceMapping
                    (CollectorId, SourceId, AssetId, TenantId, ValidFrom, ValidTo)
                VALUES
                    (@CollectorId, @SourceId, @AssetId, @TenantId, GETUTCDATE(), NULL)
                """,
                new
                {
                    CollectorId = collectorId,
                    SourceId = $"L{asset.LineIdx}-A{asset.AssetIdx}",
                    AssetId = asset.AssetId,
                    TenantId = tenantId,
                }, tx);
        }

        await tx.CommitAsync();

        if (commandChannelEnabled)
            _ = TryCreateQueuesAsync(tenantId, collectorId);

        return new CollectorRow(collectorId, tenantId, siteId, name, "Registered", commandChannelEnabled, DateTime.UtcNow);
    }

    public async Task<CollectorDetail?> GetCollectorDetailAsync(Guid collectorId)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        var collector = await conn.QuerySingleOrDefaultAsync<(
            Guid CollectorId, Guid TenantId, string TenantName, Guid SiteId, string SiteName, string RegionLabel,
            string Name, string Status, bool CommandChannelEnabled, DateTime CreatedAt,
            DateTime? LastSeenAt, string? SoftwareVersion)>(
            """
            SELECT c.CollectorId, c.TenantId, t.Name AS TenantName, c.SiteId, s.Name AS SiteName, s.RegionLabel,
                   c.Name, c.Status, c.CommandChannelEnabled, c.CreatedAt, c.LastSeenAt, c.SoftwareVersion
            FROM registry.Collector c
            JOIN registry.Tenant t ON t.TenantId = c.TenantId
            JOIN registry.Site s ON s.SiteId = c.SiteId
            WHERE c.CollectorId = @CollectorId
            """,
            new { CollectorId = collectorId });

        if (collector == default) return null;

        var config = await conn.QuerySingleOrDefaultAsync<(Guid CollectorId, int Version,
            string SettingsJson, DateTime UpdatedAt, string UpdatedBy)>(
            "SELECT CollectorId, Version, SettingsJson, UpdatedAt, UpdatedBy FROM registry.CollectorConfig WHERE CollectorId = @CollectorId",
            new { CollectorId = collectorId });

        var mappings = (await conn.QueryAsync<(string SourceId, Guid AssetId, string? AssetName,
                DateTime ValidFrom, DateTime? ValidTo)>(
            """
            SELECT m.SourceId, m.AssetId, a.Name AS AssetName, m.ValidFrom, m.ValidTo
            FROM registry.AssetSourceMapping m
            LEFT JOIN registry.Asset a ON a.AssetId = m.AssetId
            WHERE m.CollectorId = @CollectorId AND m.ValidTo IS NULL
            ORDER BY m.SourceId
            """,
            new { CollectorId = collectorId }))
            .Select(m => new MappingDetail(m.SourceId, m.AssetId, m.AssetName, m.ValidFrom, m.ValidTo))
            .ToList();

        var certificates = (await conn.QueryAsync<CertificateRow>(
            """
            SELECT Thumbprint, SerialNumber, IssuedAt, ExpiresAt, SupersededAt, RevokedAt, RevocationReason
            FROM registry.Certificate WHERE CollectorId = @CollectorId ORDER BY IssuedAt DESC
            """,
            new { CollectorId = collectorId })).AsList();

        var health = await conn.QuerySingleOrDefaultAsync<HealthRow>(
            """
            SELECT ReceivedAt, BufferDepthEvents, BufferCapacityEvents, OverflowDroppedTotal,
                   SoftwareVersion, ConfigVersion, ClockOffsetMs
            FROM registry.CollectorHealth WHERE CollectorId = @CollectorId
            """,
            new { CollectorId = collectorId });

        var from60 = DateTime.UtcNow.AddMinutes(-60);
        var reconciliation = (await conn.QueryAsync<ReconciliationRow>(
            """
            SELECT MinuteStart, ProducedCount, DroppedAtEdgeCount, AcceptedCount, DeadLetteredCount
            FROM telemetry.Reconciliation
            WHERE CollectorId = @CollectorId AND MinuteStart >= @From
            ORDER BY MinuteStart
            """,
            new { CollectorId = collectorId, From = from60 })).AsList();

        var collectorRow = new CollectorRow(
            collector.CollectorId, collector.TenantId, collector.SiteId, collector.Name,
            collector.Status, collector.CommandChannelEnabled, collector.CreatedAt);

        var configRow = config == default ? null : new CollectorConfigRow(
            config.CollectorId, config.Version, config.SettingsJson,
            collector.CommandChannelEnabled, config.UpdatedAt, config.UpdatedBy);

        return new CollectorDetail(
            collectorRow,
            collector.TenantName,
            collector.SiteName,
            collector.RegionLabel,
            collector.LastSeenAt,
            collector.SoftwareVersion,
            configRow ?? new CollectorConfigRow(collectorId, 0, "{}", collector.CommandChannelEnabled, DateTime.UtcNow, ""),
            mappings,
            certificates,
            health,
            reconciliation);
    }

    public async Task<CollectorConfigRow> UpdateConfigAsync(
        Guid collectorId, ConfigUpdateSettings settings, bool commandChannelEnabled, Actor actor)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();

        var existing = await conn.QuerySingleAsync<(int Version, string SettingsJson, bool OldEnabled, Guid TenantId)>(
            """
            SELECT cc.Version, cc.SettingsJson, c.CommandChannelEnabled, c.TenantId
            FROM registry.CollectorConfig cc
            JOIN registry.Collector c ON c.CollectorId = cc.CollectorId
            WHERE cc.CollectorId = @CollectorId
            """,
            new { CollectorId = collectorId }, tx);

        var current = JsonSerializer.Deserialize<ConfigDocument>(existing.SettingsJson, JsonOpts)
            ?? ConfigDocument.Default();

        // Merge settings
        var merged = MergeSettings(current, settings);
        var newJson = JsonSerializer.Serialize(merged, JsonOpts);
        var newVersion = existing.Version + 1;

        await conn.ExecuteAsync(
            """
            UPDATE registry.CollectorConfig
            SET Version = @Version, SettingsJson = @SettingsJson, UpdatedAt = GETUTCDATE(), UpdatedBy = @UpdatedBy
            WHERE CollectorId = @CollectorId;

            UPDATE registry.Collector
            SET CommandChannelEnabled = @CommandChannelEnabled
            WHERE CollectorId = @CollectorId;
            """,
            new
            {
                CollectorId = collectorId, Version = newVersion, SettingsJson = newJson,
                UpdatedBy = actor.Name, CommandChannelEnabled = commandChannelEnabled,
            }, tx);

        await tx.CommitAsync();

        if (commandChannelEnabled && !existing.OldEnabled)
            _ = TryCreateQueuesAsync(existing.TenantId, collectorId);
        else if (!commandChannelEnabled && existing.OldEnabled)
            _ = TryDeleteQueuesAsync(existing.TenantId, collectorId);

        return new CollectorConfigRow(collectorId, newVersion, newJson, commandChannelEnabled, DateTime.UtcNow, actor.Name);
    }

    public async Task UpdateMappingsAsync(
        Guid collectorId, IReadOnlyList<MappingInput> mappings, Actor actor)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();

        var tenantId = await conn.QuerySingleAsync<Guid>(
            "SELECT TenantId FROM registry.Collector WHERE CollectorId = @CollectorId",
            new { CollectorId = collectorId }, tx);

        var active = (await conn.QueryAsync<(string SourceId, Guid AssetId)>(
            "SELECT SourceId, AssetId FROM registry.AssetSourceMapping WHERE CollectorId = @CollectorId AND ValidTo IS NULL",
            new { CollectorId = collectorId }, tx)).ToHashSet();

        var incoming = mappings.Select(m => (m.SourceId, m.AssetId)).ToHashSet();

        // Close removed or changed
        var toClose = active.Except(incoming).Select(x => x.SourceId).ToList();
        foreach (var sourceId in toClose)
        {
            await conn.ExecuteAsync(
                "UPDATE registry.AssetSourceMapping SET ValidTo = GETUTCDATE() WHERE CollectorId = @CollectorId AND SourceId = @SourceId AND ValidTo IS NULL",
                new { CollectorId = collectorId, SourceId = sourceId }, tx);
        }

        // Insert new mappings
        var toAdd = incoming.Except(active).ToList();
        foreach (var (sourceId, assetId) in toAdd)
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO registry.AssetSourceMapping (CollectorId, SourceId, AssetId, TenantId, ValidFrom, ValidTo)
                VALUES (@CollectorId, @SourceId, @AssetId, @TenantId, GETUTCDATE(), NULL)
                """,
                new { CollectorId = collectorId, SourceId = sourceId, AssetId = assetId, TenantId = tenantId }, tx);
        }

        // Bump config version
        await conn.ExecuteAsync(
            "UPDATE registry.CollectorConfig SET Version = Version + 1, UpdatedAt = GETUTCDATE(), UpdatedBy = @UpdatedBy WHERE CollectorId = @CollectorId",
            new { CollectorId = collectorId, UpdatedBy = actor.Name }, tx);

        await tx.CommitAsync();
    }

    public async Task RevokeAsync(Guid collectorId, string reason, Actor actor)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.OpenAsync();
        await using var tx = conn.BeginTransaction();

        var (tenantId, oldStatus) = await conn.QuerySingleAsync<(Guid TenantId, string Status)>(
            "SELECT TenantId, Status FROM registry.Collector WHERE CollectorId = @CollectorId",
            new { CollectorId = collectorId }, tx);

        await conn.ExecuteAsync(
            "UPDATE registry.Certificate SET RevokedAt = GETUTCDATE(), RevocationReason = @Reason WHERE CollectorId = @CollectorId AND RevokedAt IS NULL",
            new { CollectorId = collectorId, Reason = reason }, tx);

        await conn.ExecuteAsync(
            "UPDATE registry.Collector SET Status = 'Revoked' WHERE CollectorId = @CollectorId",
            new { CollectorId = collectorId }, tx);

        await tx.CommitAsync();

        _ = TryDeleteQueuesAsync(tenantId, collectorId);

        await auditWriter.WriteAsync(actor, "CollectorRevoked", "Collector", collectorId.ToString(),
            tenantId, $"{{\"reason\":\"{reason}\"}}");
    }

    public async Task<IReadOnlyList<HealthHistoryRow>> GetHealthHistoryAsync(
        Guid collectorId, DateTime? from, DateTime? to)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        var rows = await conn.QueryAsync<HealthHistoryRow>(
            """
            SELECT ReceivedAt, BufferDepthEvents, BufferCapacityEvents, OverflowDroppedTotal
            FROM registry.CollectorHealthHistory
            WHERE CollectorId = @CollectorId
              AND (@From IS NULL OR ReceivedAt >= @From)
              AND (@To IS NULL OR ReceivedAt <= @To)
            ORDER BY ReceivedAt
            """,
            new { CollectorId = collectorId, From = from, To = to });
        return rows.AsList();
    }

    public async Task<(Guid tenantId, int healthIntervalSeconds)?> GetCollectorTenantInfoAsync(Guid collectorId)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        var row = await conn.QuerySingleOrDefaultAsync<(Guid TenantId, string? SettingsJson)>(
            """
            SELECT c.TenantId, cc.SettingsJson
            FROM registry.Collector c
            LEFT JOIN registry.CollectorConfig cc ON cc.CollectorId = c.CollectorId
            WHERE c.CollectorId = @CollectorId
            """,
            new { CollectorId = collectorId });

        if (row == default) return null;

        var interval = 60;
        if (row.SettingsJson != null)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<ConfigDocument>(row.SettingsJson, JsonOpts);
                if (doc?.Intervals?.HealthReportSeconds > 0)
                    interval = doc.Intervals.HealthReportSeconds;
            }
            catch { /* use default */ }
        }

        return (row.TenantId, interval);
    }

    private async Task TryCreateQueuesAsync(Guid tenantId, Guid collectorId)
    {
        try { await queueManager.EnsureQueuesAsync(tenantId, collectorId); }
        catch { /* reconciler retries */ }
    }

    private async Task TryDeleteQueuesAsync(Guid tenantId, Guid collectorId)
    {
        try { await queueManager.DeleteQueuesAsync(tenantId, collectorId); }
        catch { /* best effort */ }
    }

    private static ConfigDocument MergeSettings(ConfigDocument current, ConfigUpdateSettings input)
    {
        var batching = input.Batching == null ? current.Batching : new BatchingSettings(
            input.Batching.MaxSize ?? current.Batching.MaxSize,
            input.Batching.MaxAgeSeconds ?? current.Batching.MaxAgeSeconds);

        var forwarder = input.Forwarder == null ? current.Forwarder : new ForwarderSettings(
            input.Forwarder.MaxConcurrentBatches ?? current.Forwarder.MaxConcurrentBatches,
            input.Forwarder.RetryDelaySeconds ?? current.Forwarder.RetryDelaySeconds);

        var buffer = input.Buffer == null ? current.Buffer : new BufferSettings(
            input.Buffer.CapacityEvents ?? current.Buffer.CapacityEvents,
            input.Buffer.OverflowStrategy ?? current.Buffer.OverflowStrategy);

        var intervals = input.Intervals == null ? current.Intervals : new IntervalsSettings(
            input.Intervals.HealthReportSeconds ?? current.Intervals.HealthReportSeconds,
            input.Intervals.ConfigPollSeconds ?? current.Intervals.ConfigPollSeconds);

        return new ConfigDocument(batching, forwarder, buffer, intervals);
    }
}
