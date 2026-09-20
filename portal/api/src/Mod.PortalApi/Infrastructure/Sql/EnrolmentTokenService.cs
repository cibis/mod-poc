using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Infrastructure.Sql;

internal sealed class EnrolmentTokenService(SqlConfig sqlConfig) : IEnrolmentTokenService
{
    public async Task<EnrolmentTokenResult> IssueTokenAsync(Guid collectorId, Actor actor)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Base64UrlEncode(tokenBytes);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);

        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.ExecuteAsync(
            """
            INSERT INTO registry.EnrolmentToken (TokenHash, CollectorId, CreatedAt, ExpiresAt, CreatedBy)
            VALUES (@TokenHash, @CollectorId, GETUTCDATE(), @ExpiresAt, @CreatedBy)
            """,
            new
            {
                TokenHash = hash,
                CollectorId = collectorId,
                ExpiresAt = expiresAt.UtcDateTime,
                CreatedBy = actor.Name,
            });

        return new EnrolmentTokenResult(token, expiresAt);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
