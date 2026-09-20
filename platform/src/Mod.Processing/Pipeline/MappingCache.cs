using Dapper;
using Mod.Platform.Common.Sql;

namespace Mod.Processing.Pipeline;

internal record AssetMapping(Guid AssetId, Guid LineId, Guid TenantId, Guid SiteId);

internal sealed class MappingCache(SqlConnectionFactory sqlFactory, ProcessingOptions options)
{
    private readonly Dictionary<(Guid, string), (AssetMapping? Mapping, DateTime Expires)> _cache = new();
    private readonly object _lock = new();

    public async Task<AssetMapping?> GetAsync(Guid collectorId, string sourceId, CancellationToken ct)
    {
        var key = (collectorId, sourceId);

        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.Expires > DateTime.UtcNow)
                return entry.Mapping;
        }

        const string sql = """
            SELECT m.AssetId, a.LineId, m.TenantId, a.SiteId
            FROM registry.AssetSourceMapping m
            JOIN registry.Asset a ON a.AssetId = m.AssetId
            WHERE m.CollectorId = @collectorId
              AND m.SourceId = @sourceId
              AND m.ValidTo IS NULL
            """;

        await using var conn = sqlFactory.CreateConnection();
        var mapping = await conn.QuerySingleOrDefaultAsync<AssetMapping>(
            new CommandDefinition(sql, new { collectorId, sourceId }, cancellationToken: ct));

        var expires = DateTime.UtcNow.AddSeconds(options.MappingCacheSeconds);
        lock (_lock)
        {
            _cache[key] = (mapping, expires);
        }
        return mapping;
    }
}
