using System.Security.Claims;
using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Features.Admin.DeadLetters;

internal static class DeadLetterEndpoints
{
    internal static IEndpointRouteBuilder MapDeadLetters(this IEndpointRouteBuilder app)
    {
        app.MapGet("/dead-letters", ListDeadLetters);
        app.MapGet("/dead-letters/{id:long}", GetDeadLetter);
        app.MapPost("/dead-letters/replay", ReplayDeadLetters);
        app.MapPost("/dead-letters/discard", DiscardDeadLetters);
        return app;
    }

    private static async Task<IResult> ListDeadLetters(
        SqlConfig sqlConfig,
        Guid? tenantId, Guid? collectorId, string? reasonCode, string? status,
        int page = 1, int pageSize = 50)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        const string where = """
            WHERE (@TenantId IS NULL OR TenantId = @TenantId)
              AND (@CollectorId IS NULL OR CollectorId = @CollectorId)
              AND (@ReasonCode IS NULL OR ReasonCode = @ReasonCode)
              AND (@Status IS NULL OR Status = @Status)
            """;
        var p = new { TenantId = tenantId, CollectorId = collectorId, ReasonCode = reasonCode, Status = status };

        var total = await conn.QuerySingleAsync<int>($"SELECT COUNT(*) FROM telemetry.DeadLetter {where}", p);
        var offset = (page - 1) * pageSize;
        var items = (await conn.QueryAsync(
            $"""
            SELECT DeadLetterId, TenantId, SiteId, CollectorId, BatchId, EventId,
                   ReasonCode, ReasonDetail, ReceivedAt, CreatedAt, Status, StatusChangedAt,
                   StatusChangedBy, ReplayAttempts
            FROM telemetry.DeadLetter
            {where}
            ORDER BY CreatedAt DESC
            OFFSET {offset} ROWS FETCH NEXT {pageSize} ROWS ONLY
            """, p)).AsList();

        return Results.Ok(new PagedResult<dynamic>(items, total));
    }

    private static async Task<IResult> GetDeadLetter(long id, SqlConfig sqlConfig)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        var row = await conn.QuerySingleOrDefaultAsync(
            "SELECT * FROM telemetry.DeadLetter WHERE DeadLetterId = @Id",
            new { Id = id });
        return row is null ? Results.NotFound() : Results.Ok(row);
    }

    private static async Task<IResult> ReplayDeadLetters(
        ReplayRequest req, SqlConfig sqlConfig,
        IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        int updated;
        if (req.Ids is { Count: > 0 })
        {
            updated = await conn.ExecuteAsync(
                """
                UPDATE telemetry.DeadLetter
                SET Status = 'ReplayRequested', StatusChangedAt = GETUTCDATE(), StatusChangedBy = @By
                WHERE DeadLetterId IN @Ids AND Status = 'New'
                """,
                new { Ids = req.Ids, By = actor.Name });
        }
        else if (req.Filter is not null)
        {
            updated = await conn.ExecuteAsync(
                """
                UPDATE telemetry.DeadLetter
                SET Status = 'ReplayRequested', StatusChangedAt = GETUTCDATE(), StatusChangedBy = @By
                WHERE Status = 'New'
                  AND (@TenantId IS NULL OR TenantId = @TenantId)
                  AND (@CollectorId IS NULL OR CollectorId = @CollectorId)
                  AND (@ReasonCode IS NULL OR ReasonCode = @ReasonCode)
                """,
                new
                {
                    By = actor.Name,
                    TenantId = req.Filter.TenantId,
                    CollectorId = req.Filter.CollectorId,
                    ReasonCode = req.Filter.ReasonCode,
                });
        }
        else
        {
            return Results.BadRequest();
        }

        await audit.WriteAsync(actor, "DeadLettersReplayed", "DeadLetter", "*", null,
            $"{{\"updated\":{updated}}}");
        return Results.Ok(new { Updated = updated });
    }

    private static async Task<IResult> DiscardDeadLetters(
        DiscardRequest req, SqlConfig sqlConfig,
        IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);

        var updated = await conn.ExecuteAsync(
            """
            UPDATE telemetry.DeadLetter
            SET Status = 'Discarded', StatusChangedAt = GETUTCDATE(), StatusChangedBy = @By
            WHERE DeadLetterId IN @Ids AND Status = 'New'
            """,
            new { Ids = req.Ids, By = actor.Name });

        await audit.WriteAsync(actor, "DeadLettersDiscarded", "DeadLetter", "*", null,
            $"{{\"updated\":{updated}}}");
        return Results.Ok(new { Updated = updated });
    }

    private static Actor GetActor(ClaimsPrincipal user) =>
        new(user.FindFirstValue("name") ?? "Unknown", user.FindFirstValue("kind") ?? "ModAdmin");

    private sealed record ReplayRequest(IReadOnlyList<long>? Ids, DeadLetterFilter? Filter);
    private sealed record DeadLetterFilter(Guid? TenantId, Guid? CollectorId, string? ReasonCode);
    private sealed record DiscardRequest(IReadOnlyList<long> Ids);
}
