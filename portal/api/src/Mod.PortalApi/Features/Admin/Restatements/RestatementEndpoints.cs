using Dapper;
using Microsoft.Data.SqlClient;

namespace Mod.PortalApi.Features.Admin.Restatements;

internal static class RestatementEndpoints
{
    internal static IEndpointRouteBuilder MapRestatements(this IEndpointRouteBuilder app)
    {
        app.MapGet("/restatements", GetRestatements);
        return app;
    }

    private static async Task<IResult> GetRestatements(
        SqlConfig sqlConfig,
        Guid? tenantId, DateTime? from, DateTime? to)
    {
        await using var conn = new SqlConnection(sqlConfig.ConnectionString);
        var rows = await conn.QueryAsync(
            """
            SELECT
                rm.TenantId, rm.SiteId, rm.LineId, rm.AssetId,
                a.Name AS AssetName, l.Name AS LineName, s.Name AS SiteName,
                rm.MinuteStart, rm.EventCount, rm.CounterDelta, rm.FaultEventCount,
                rm.LastState, rm.ProcessValueAvg, rm.ProcessValueMin, rm.ProcessValueMax,
                rm.RestatementCount, rm.FirstComputedAt, rm.LastComputedAt
            FROM telemetry.RollupMinute rm
            JOIN registry.Asset a ON a.AssetId = rm.AssetId
            JOIN registry.Line l ON l.LineId = rm.LineId
            JOIN registry.Site s ON s.SiteId = rm.SiteId
            WHERE rm.IsRestated = 1
              AND (@TenantId IS NULL OR rm.TenantId = @TenantId)
              AND (@From IS NULL OR rm.MinuteStart >= @From)
              AND (@To IS NULL OR rm.MinuteStart <= @To)
            ORDER BY rm.MinuteStart DESC
            """,
            new { TenantId = tenantId, From = from, To = to });
        return Results.Ok(rows);
    }
}
