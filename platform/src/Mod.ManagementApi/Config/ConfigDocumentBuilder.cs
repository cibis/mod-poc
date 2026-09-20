using System.Text.Json;
using Dapper;
using Mod.Platform.Common.Sql;

namespace Mod.ManagementApi.Config;

public sealed class ConfigDocumentBuilder(SqlConnectionFactory db, ConfigDocumentBuilder.Options opts)
{
    public sealed record Options(string IngestUrl, string ManagementUrl);

    public async Task<ConfigDocument?> BuildAsync(Guid collectorId, CancellationToken ct = default)
    {
        await using var conn = db.CreateConnection();

        const string collectorSql = """
            SELECT c.TenantId, c.SiteId, c.CommandChannelEnabled,
                   cc.Version AS ConfigVersion, cc.SettingsJson
            FROM registry.Collector c
            LEFT JOIN registry.CollectorConfig cc ON cc.CollectorId = c.CollectorId
            WHERE c.CollectorId = @collectorId
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(collectorSql, new { collectorId }, cancellationToken: ct));

        if (row is null) return null;

        const string sourcesSql = """
            SELECT asm.SourceId, a.Name AS AssetName, a.AssetType, a.CounterName,
                   a.ProcessValueName, a.ProcessValueUnit, a.ProcessValueMin, a.ProcessValueMax
            FROM registry.AssetSourceMapping asm
            JOIN registry.Asset a ON a.AssetId = asm.AssetId
            WHERE asm.CollectorId = @collectorId AND asm.ValidTo IS NULL
            """;

        var sourceRows = await conn.QueryAsync(
            new CommandDefinition(sourcesSql, new { collectorId }, cancellationToken: ct));

        var sources = sourceRows.Select(s =>
        {
            ProcessValueConfig? pv = null;
            if (!string.IsNullOrEmpty((string?)s.ProcessValueName))
            {
                pv = new ProcessValueConfig(
                    (string)s.ProcessValueName,
                    (string?)s.ProcessValueUnit ?? "",
                    (double?)s.ProcessValueMin,
                    (double?)s.ProcessValueMax);
            }
            return new SourceConfig(
                (string)s.SourceId,
                (string)s.AssetName,
                (string)s.AssetType,
                (string)s.CounterName,
                pv);
        }).ToList();

        var (batching, forwarder, buffer, intervals) = MergeSettings((string?)row.SettingsJson);

        return new ConfigDocument(
            ConfigVersion: (int?)row.ConfigVersion ?? 0,
            CollectorId: collectorId,
            TenantId: (Guid)row.TenantId,
            SiteId: (Guid)row.SiteId,
            IngestUrl: opts.IngestUrl,
            ManagementUrl: opts.ManagementUrl,
            Sources: sources,
            Batching: batching,
            Forwarder: forwarder,
            Buffer: buffer,
            Intervals: intervals,
            CommandChannelEnabled: (bool)row.CommandChannelEnabled);
    }

    private static (BatchingConfig, ForwarderConfig, BufferConfig, IntervalsConfig) MergeSettings(
        string? settingsJson)
    {
        var batching = new BatchingConfig(500, 262144, 2000);
        var forwarder = new ForwarderConfig(5, 1000, 60000);
        var buffer = new BufferConfig(200000);
        var intervals = new IntervalsConfig(30, 30);

        if (string.IsNullOrWhiteSpace(settingsJson))
            return (batching, forwarder, buffer, intervals);

        try
        {
            using var doc = JsonDocument.Parse(settingsJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("batching", out var b))
                batching = new BatchingConfig(
                    GetInt(b, "maxEvents", batching.MaxEvents),
                    GetInt(b, "maxBytes", batching.MaxBytes),
                    GetInt(b, "flushIntervalMs", batching.FlushIntervalMs));

            if (root.TryGetProperty("forwarder", out var f))
                forwarder = new ForwarderConfig(
                    GetInt(f, "maxBatchesPerSecond", forwarder.MaxBatchesPerSecond),
                    GetInt(f, "retryBaseMs", forwarder.RetryBaseMs),
                    GetInt(f, "retryMaxMs", forwarder.RetryMaxMs));

            if (root.TryGetProperty("buffer", out var buf))
                buffer = new BufferConfig(GetInt(buf, "capacityEvents", buffer.CapacityEvents));

            if (root.TryGetProperty("intervals", out var iv))
                intervals = new IntervalsConfig(
                    GetInt(iv, "configPollSeconds", intervals.ConfigPollSeconds),
                    GetInt(iv, "healthReportSeconds", intervals.HealthReportSeconds));
        }
        catch (JsonException) { }

        return (batching, forwarder, buffer, intervals);
    }

    private static int GetInt(JsonElement el, string name, int fallback) =>
        el.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;
}
