using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Infrastructure.Sql;

internal sealed class CertificateService(SqlConfig sqlConfig) : ICertificateService
{
    public async Task RevokeAllActiveAsync(Guid collectorId, string reason)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.ExecuteAsync(
            """
            UPDATE registry.Certificate
            SET RevokedAt = GETUTCDATE(), RevocationReason = @Reason
            WHERE CollectorId = @CollectorId AND RevokedAt IS NULL
            """,
            new { CollectorId = collectorId, Reason = reason });
    }
}
