using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Infrastructure.Sql;

internal sealed class AuditWriter(SqlConfig sqlConfig) : IAuditWriter
{
    public async Task WriteAsync(Actor actor, string action, string targetType, string targetId,
        Guid? tenantId, string? detailsJson = null)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        await conn.ExecuteAsync(
            """
            INSERT INTO registry.AuditLog
                (At, ActorName, ActorKind, Action, TargetType, TargetId, TenantId, DetailsJson)
            VALUES
                (GETUTCDATE(), @ActorName, @ActorKind, @Action, @TargetType, @TargetId, @TenantId, @DetailsJson)
            """,
            new
            {
                ActorName = actor.Name,
                ActorKind = actor.Kind,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                TenantId = tenantId,
                DetailsJson = detailsJson,
            });
    }
}
