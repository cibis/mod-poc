using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Mod.Platform.Common.Sql;

namespace Mod.Platform.Common.Registry;

public sealed class RegistryReader(SqlConnectionFactory connectionFactory, IMemoryCache cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task<CollectorRecord?> GetCollectorByThumbprintAsync(
        string thumbprint,
        CancellationToken ct = default)
    {
        var key = $"collector:{thumbprint}";
        if (cache.TryGetValue(key, out CollectorRecord? cached))
            return cached;

        const string sql = """
            SELECT c.CollectorId, c.TenantId, c.SiteId, c.Status
            FROM registry.Certificate cert
            JOIN registry.Collector c ON c.CollectorId = cert.CollectorId
            WHERE cert.Thumbprint = @thumbprint
            """;

        await using var conn = connectionFactory.CreateConnection();
        var record = await conn.QuerySingleOrDefaultAsync<CollectorRecord>(
            new CommandDefinition(sql, new { thumbprint }, cancellationToken: ct));

        cache.Set(key, record, CacheTtl);
        return record;
    }

    public async Task<CertificateRecord?> GetCertificateByThumbprintAsync(
        string thumbprint,
        CancellationToken ct = default)
    {
        var key = $"cert:{thumbprint}";
        if (cache.TryGetValue(key, out CertificateRecord? cached))
            return cached;

        const string sql = """
            SELECT Thumbprint, RevokedAt
            FROM registry.Certificate
            WHERE Thumbprint = @thumbprint
            """;

        await using var conn = connectionFactory.CreateConnection();
        var record = await conn.QuerySingleOrDefaultAsync<CertificateRecord>(
            new CommandDefinition(sql, new { thumbprint }, cancellationToken: ct));

        cache.Set(key, record, CacheTtl);
        return record;
    }
}
