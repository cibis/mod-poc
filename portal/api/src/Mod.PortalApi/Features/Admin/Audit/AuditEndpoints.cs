using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Models;

namespace Mod.PortalApi.Features.Admin.Audit;

internal static class AuditEndpoints
{
    internal static IEndpointRouteBuilder MapAudit(this IEndpointRouteBuilder app)
    {
        app.MapGet("/audit", GetAudit);
        return app;
    }

    private static async Task<IResult> GetAudit(
        SqlConfig sqlConfig,
        Guid? tenantId, DateTime? from, DateTime? to, string? action,
        int page = 1, int pageSize = 50)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        const string where = """
            WHERE (@TenantId IS NULL OR TenantId = @TenantId)
              AND (@From IS NULL OR At >= @From)
              AND (@To IS NULL OR At <= @To)
              AND (@Action IS NULL OR Action = @Action)
            """;
        var p = new { TenantId = tenantId, From = from, To = to, Action = action };

        var total = await conn.QuerySingleAsync<int>($"SELECT COUNT(*) FROM registry.AuditLog {where}", p);
        var offset = (page - 1) * pageSize;
        var items = (await conn.QueryAsync(
            $"""
            SELECT AuditId, At, ActorName, ActorKind, Action, TargetType, TargetId, TenantId, DetailsJson
            FROM registry.AuditLog
            {where}
            ORDER BY At DESC
            OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY
            """, p)).AsList();

        return Results.Ok(new PagedResult<dynamic>(items, total));
    }
}
