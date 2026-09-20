using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Mod.ManagementApi.Auth;
using Mod.ManagementApi.Models;
using Mod.Platform.Common.Certificates;
using Mod.Platform.Common.Sql;

namespace Mod.ManagementApi.Endpoints;

public static class HealthEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        HealthReport report,
        CertificateValidator certValidator,
        SqlConnectionFactory db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var (err, identity) = await CertAuth.AuthenticateAsync(context, certValidator, ct);
        if (err is not null) return err;

        if (report.CollectorId != identity!.CollectorId)
            return Results.Problem(statusCode: 400, type: "CollectorMismatch");

        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var validMinutes = report.MinuteCounts
            .Where(m => m.MinuteStart < now && m.MinuteStart >= sevenDaysAgo)
            .ToList();

        var reportJson = JsonSerializer.Serialize(report);

        await using var conn = db.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, ct);

        // Upsert CollectorHealth (latest)
        await conn.ExecuteAsync(new CommandDefinition("""
            MERGE registry.CollectorHealth WITH (HOLDLOCK) AS t
            USING (SELECT @CollectorId, @ReceivedAt, @BufferDepthEvents, @BufferCapacityEvents,
                          @OverflowDroppedTotal, @SoftwareVersion, @ConfigVersion, @ClockOffsetMs,
                          @ReportJson) AS s(
                          CollectorId, ReceivedAt, BufferDepthEvents, BufferCapacityEvents,
                          OverflowDroppedTotal, SoftwareVersion, ConfigVersion, ClockOffsetMs, ReportJson)
            ON t.CollectorId = s.CollectorId
            WHEN MATCHED THEN UPDATE SET
                ReceivedAt = s.ReceivedAt,
                BufferDepthEvents = s.BufferDepthEvents,
                BufferCapacityEvents = s.BufferCapacityEvents,
                OverflowDroppedTotal = s.OverflowDroppedTotal,
                SoftwareVersion = s.SoftwareVersion,
                ConfigVersion = s.ConfigVersion,
                ClockOffsetMs = s.ClockOffsetMs,
                ReportJson = s.ReportJson
            WHEN NOT MATCHED THEN INSERT
                (CollectorId, ReceivedAt, BufferDepthEvents, BufferCapacityEvents,
                 OverflowDroppedTotal, SoftwareVersion, ConfigVersion, ClockOffsetMs, ReportJson)
            VALUES
                (s.CollectorId, s.ReceivedAt, s.BufferDepthEvents, s.BufferCapacityEvents,
                 s.OverflowDroppedTotal, s.SoftwareVersion, s.ConfigVersion, s.ClockOffsetMs, s.ReportJson);
            """,
            new
            {
                CollectorId = identity.CollectorId,
                ReceivedAt = now,
                report.BufferDepthEvents,
                report.BufferCapacityEvents,
                report.OverflowDroppedTotal,
                report.SoftwareVersion,
                report.ConfigVersion,
                report.ClockOffsetMs,
                ReportJson = reportJson
            }, tx, cancellationToken: ct));

        // Insert health history row
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO registry.CollectorHealthHistory
                (CollectorId, ReceivedAt, BufferDepthEvents, BufferCapacityEvents, OverflowDroppedTotal)
            VALUES (@CollectorId, @ReceivedAt, @BufferDepthEvents, @BufferCapacityEvents, @OverflowDroppedTotal)
            """,
            new
            {
                CollectorId = identity.CollectorId,
                ReceivedAt = now,
                report.BufferDepthEvents,
                report.BufferCapacityEvents,
                report.OverflowDroppedTotal
            }, tx, cancellationToken: ct));

        // Upsert Reconciliation for each valid minute
        foreach (var m in validMinutes)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                MERGE telemetry.Reconciliation WITH (HOLDLOCK) AS t
                USING (SELECT @CollectorId, @MinuteStart, @TenantId, @ProducedCount, @DroppedAtEdgeCount)
                    AS s(CollectorId, MinuteStart, TenantId, ProducedCount, DroppedAtEdgeCount)
                ON t.CollectorId = s.CollectorId AND t.MinuteStart = s.MinuteStart
                WHEN MATCHED THEN UPDATE SET
                    ProducedCount = s.ProducedCount,
                    DroppedAtEdgeCount = s.DroppedAtEdgeCount
                WHEN NOT MATCHED THEN INSERT
                    (CollectorId, MinuteStart, TenantId, ProducedCount, DroppedAtEdgeCount)
                VALUES
                    (s.CollectorId, s.MinuteStart, s.TenantId, s.ProducedCount, s.DroppedAtEdgeCount);
                """,
                new
                {
                    CollectorId = identity.CollectorId,
                    MinuteStart = m.MinuteStart,
                    TenantId = identity.TenantId,
                    ProducedCount = m.Produced,
                    DroppedAtEdgeCount = m.DroppedAtEdge
                }, tx, cancellationToken: ct));
        }

        // Update Collector fields
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE registry.Collector
            SET LastSeenAt = @Now,
                SoftwareVersion = @SoftwareVersion,
                ReportedConfigVersion = @ConfigVersion,
                ReportedConfigHash = @ConfigHash
            WHERE CollectorId = @CollectorId
            """,
            new
            {
                Now = now,
                report.SoftwareVersion,
                report.ConfigVersion,
                ConfigHash = report.ConfigHash,
                CollectorId = identity.CollectorId
            }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return Results.NoContent();
    }
}
