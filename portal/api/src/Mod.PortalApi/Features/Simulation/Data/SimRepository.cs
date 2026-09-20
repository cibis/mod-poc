// DEMO SCAFFOLDING
using Dapper;
using Microsoft.Data.SqlClient;
using Mod.PortalApi.Features.Simulation.Models;

namespace Mod.PortalApi.Features.Simulation.Data;

internal sealed class SimRepository(SqlConfig config)
{
    private SqlConnection Connect() => new(config.ConnectionString);

    // ── CollectorState ──────────────────────────────────────────────────────

    internal async Task<IReadOnlyList<CollectorStateRow>> GetAllCollectorStatesAsync()
    {
        await using var conn = Connect();
        var rows = await conn.QueryAsync<CollectorStateRow>(
            """
            SELECT CollectorId, PoweredOn, ContainerAppName, ProvisioningState,
                   LastError, BufferOverflowEver, LastStatusJson, LastStatusAt, UpdatedAt
            FROM sim.CollectorState
            """);
        return rows.AsList();
    }

    internal async Task SetPowerOnStateAsync(Guid collectorId, string appName)
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            """
            UPDATE sim.CollectorState
            SET PoweredOn = 1, ContainerAppName = @appName, ProvisioningState = 'Provisioning',
                LastError = NULL, UpdatedAt = GETUTCDATE()
            WHERE CollectorId = @collectorId
            """,
            new { collectorId, appName });
    }

    internal async Task SetPowerOffStateAsync(Guid collectorId)
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            """
            UPDATE sim.CollectorState
            SET PoweredOn = 0, ContainerAppName = NULL, ProvisioningState = 'Off',
                LastError = NULL, UpdatedAt = GETUTCDATE()
            WHERE CollectorId = @collectorId
            """,
            new { collectorId });
    }

    internal async Task SetProvisioningStateAsync(Guid collectorId, string state, string? error)
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            """
            UPDATE sim.CollectorState
            SET ProvisioningState = @state, LastError = @error, UpdatedAt = GETUTCDATE()
            WHERE CollectorId = @collectorId
            """,
            new { collectorId, state, error });
    }

    internal async Task SetBufferOverflowEverAsync(Guid collectorId)
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            """
            UPDATE sim.CollectorState
            SET BufferOverflowEver = 1, UpdatedAt = GETUTCDATE()
            WHERE CollectorId = @collectorId
            """,
            new { collectorId });
    }

    internal async Task UpdateLastStatusAsync(Guid collectorId, string json, DateTime at)
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            """
            UPDATE sim.CollectorState
            SET LastStatusJson = @json, LastStatusAt = @at, UpdatedAt = GETUTCDATE()
            WHERE CollectorId = @collectorId
            """,
            new { collectorId, json, at });
    }

    // ── TimelineMarker ───────────────────────────────────────────────────────

    internal async Task<long> InsertTimelineMarkerAsync(
        string kind, string label, IReadOnlyList<Guid>? targets, Guid? scenarioRunId)
    {
        await using var conn = Connect();
        var targetsJson = targets is { Count: > 0 }
            ? System.Text.Json.JsonSerializer.Serialize(targets)
            : null;
        return await conn.ExecuteScalarAsync<long>(
            """
            INSERT INTO sim.TimelineMarker (At, Kind, Label, TargetsJson, ScenarioRunId)
            OUTPUT INSERTED.MarkerId
            VALUES (GETUTCDATE(), @kind, @label, @targetsJson, @scenarioRunId)
            """,
            new { kind, label, targetsJson, scenarioRunId });
    }

    internal async Task<IReadOnlyList<TimelineMarkerRow>> QueryTimelineAsync(DateTime from, DateTime to)
    {
        await using var conn = Connect();
        var rows = await conn.QueryAsync<TimelineMarkerRow>(
            """
            SELECT MarkerId, At, Kind, Label, TargetsJson, ScenarioRunId
            FROM sim.TimelineMarker
            WHERE At >= @from AND At <= @to
            ORDER BY At
            """,
            new { from, to });
        return rows.AsList();
    }

    // ── ScenarioRun ──────────────────────────────────────────────────────────

    internal async Task InsertScenarioRunAsync(
        Guid runId, string name, string parametersJson, string startedBy)
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            """
            INSERT INTO sim.ScenarioRun (ScenarioRunId, ScenarioName, ParametersJson, StartedAt, Status, StartedBy)
            VALUES (@runId, @name, @parametersJson, GETUTCDATE(), 'Running', @startedBy)
            """,
            new { runId, name, parametersJson, startedBy });
    }

    internal async Task UpdateScenarioRunAsync(Guid runId, string status, DateTime? endedAt)
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            "UPDATE sim.ScenarioRun SET Status = @status, EndedAt = @endedAt WHERE ScenarioRunId = @runId",
            new { runId, status, endedAt });
    }

    internal async Task<IReadOnlyList<ScenarioRunRow>> GetRecentScenarioRunsAsync(int limit)
    {
        await using var conn = Connect();
        var rows = await conn.QueryAsync<ScenarioRunRow>(
            """
            SELECT TOP (@limit) ScenarioRunId, ScenarioName, ParametersJson,
                                StartedAt, EndedAt, Status, StartedBy
            FROM sim.ScenarioRun
            ORDER BY StartedAt DESC
            """,
            new { limit });
        return rows.AsList();
    }

    internal async Task<ScenarioRunRow?> GetScenarioRunAsync(Guid runId)
    {
        await using var conn = Connect();
        return await conn.QuerySingleOrDefaultAsync<ScenarioRunRow>(
            """
            SELECT ScenarioRunId, ScenarioName, ParametersJson, StartedAt, EndedAt, Status, StartedBy
            FROM sim.ScenarioRun WHERE ScenarioRunId = @runId
            """,
            new { runId });
    }

    internal async Task<bool> HasActiveScenarioRunAsync()
    {
        await using var conn = Connect();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sim.ScenarioRun WHERE Status = 'Running'") > 0;
    }

    // ── MetricSample ─────────────────────────────────────────────────────────

    internal async Task InsertMetricSamplesAsync(IEnumerable<(string Key, double Value)> samples)
    {
        var at = new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        await using var conn = Connect();
        foreach (var (key, value) in samples)
        {
            await conn.ExecuteAsync(
                """
                MERGE sim.MetricSample AS t
                USING (SELECT @key AS MetricKey, @at AS At) AS s
                  ON t.MetricKey = s.MetricKey AND t.At = s.At
                WHEN MATCHED THEN UPDATE SET t.Value = @value
                WHEN NOT MATCHED THEN INSERT (MetricKey, At, Value) VALUES (@key, @at, @value);
                """,
                new { key, at, value });
        }
    }

    internal async Task<IReadOnlyList<(string Key, long AtMs, double Value)>> QueryMetricSamplesAsync(
        IReadOnlyList<string> keys, DateTime from, DateTime to)
    {
        await using var conn = Connect();
        var rows = await conn.QueryAsync<(string Key, long AtMs, double Value)>(
            """
            SELECT MetricKey AS Key,
                   DATEDIFF_BIG(MILLISECOND, '1970-01-01 00:00:00', At) AS AtMs,
                   Value
            FROM sim.MetricSample
            WHERE MetricKey IN @keys AND At >= @from AND At <= @to
            ORDER BY MetricKey, At
            """,
            new { keys, from, to });
        return rows.AsList();
    }

    internal async Task PurgeOldMetricSamplesAsync()
    {
        await using var conn = Connect();
        await conn.ExecuteAsync(
            "DELETE FROM sim.MetricSample WHERE At < DATEADD(HOUR, -24, GETUTCDATE())");
    }

    // ── Telemetry reads (portal reads only; platform owns these tables) ───────

    internal async Task<(long TotalProcessed, long TotalDeadLettered)> GetFreshnessTotalsAsync()
    {
        await using var conn = Connect();
        var row = await conn.QuerySingleOrDefaultAsync<(long TotalProcessed, long TotalDeadLettered)>(
            """
            SELECT ISNULL(SUM(EventsProcessedTotal), 0)    AS TotalProcessed,
                   ISNULL(SUM(EventsDeadLetteredTotal), 0) AS TotalDeadLettered
            FROM telemetry.CollectorFreshness
            """);
        return row;
    }

    internal async Task<IReadOnlyList<(Guid CollectorId, long FreshnessAgeSec)>> GetFreshnessAgesAsync(
        IReadOnlyList<Guid> collectorIds)
    {
        if (collectorIds.Count == 0) return Array.Empty<(Guid, long)>();
        await using var conn = Connect();
        var rows = await conn.QueryAsync<(Guid CollectorId, long FreshnessAgeSec)>(
            """
            SELECT CollectorId,
                   DATEDIFF(SECOND, LastEventTime, GETUTCDATE()) AS FreshnessAgeSec
            FROM telemetry.CollectorFreshness
            WHERE CollectorId IN @collectorIds
            """,
            new { collectorIds });
        return rows.AsList();
    }

    internal async Task<int> GetRestatedRollupCountLastHourAsync()
    {
        await using var conn = Connect();
        return await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(1) FROM telemetry.RollupMinute
            WHERE IsRestated = 1 AND LastComputedAt >= DATEADD(HOUR, -1, GETUTCDATE())
            """);
    }

    internal async Task<IReadOnlyList<(Guid TenantId, double Completeness)>> GetTenantCompletenessAsync()
    {
        await using var conn = Connect();
        var rows = await conn.QueryAsync<(Guid TenantId, double Completeness)>(
            """
            SELECT TenantId,
                   ISNULL(
                       SUM(CAST(AcceptedCount + DeadLetteredCount AS float))
                       / NULLIF(SUM(CAST(ProducedCount AS float)), 0),
                   0) AS Completeness
            FROM telemetry.Reconciliation
            WHERE MinuteStart >= DATEADD(HOUR, -1, GETUTCDATE())
            GROUP BY TenantId
            """);
        return rows.AsList();
    }
}
