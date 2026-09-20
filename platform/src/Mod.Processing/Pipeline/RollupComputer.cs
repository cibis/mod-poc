using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;

namespace Mod.Processing.Pipeline;

internal static class RollupComputer
{
    private record RawRow(
        string EventType,
        string PayloadJson,
        DateTime EventTime,
        Guid CollectorId,
        string SourceId);

    public static async Task RecomputeAsync(
        SqlConnection conn, SqlTransaction tx,
        Guid assetId, Guid tenantId, Guid siteId, Guid lineId,
        DateTime minuteStart,
        int windowGraceSeconds,
        DateTime now,
        CancellationToken ct)
    {
        var minuteEnd = minuteStart.AddMinutes(1);

        // Load all events in the minute for this asset (within the transaction so BulkCopy is visible)
        var rows = (await conn.QueryAsync<RawRow>(
            new CommandDefinition("""
                SELECT EventType, PayloadJson, EventTime, CollectorId, SourceId
                FROM telemetry.RawEvent
                WHERE AssetId = @assetId
                  AND EventTime >= @minuteStart
                  AND EventTime < @minuteEnd
                ORDER BY EventTime
                """,
                new { assetId, minuteStart, minuteEnd }, tx, cancellationToken: ct))).ToList();

        if (rows.Count == 0) return;

        var eventCount = rows.Count;
        var faultCount = 0;
        string? lastState = null;
        double? pvSum = null, pvMin = null, pvMax = null;
        int pvCount = 0;
        long counterDelta = 0;

        // Counter: unique (CollectorId, SourceId, name) in this minute
        var counterKeys = rows
            .Where(r => r.EventType == "Counter")
            .Select(r =>
            {
                using var doc = JsonDocument.Parse(r.PayloadJson);
                var name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                return (r.CollectorId, r.SourceId, Name: name);
            })
            .Distinct()
            .ToList();

        // For each counter key, get the last value before this minute
        var prevCounterValues = new Dictionary<(Guid, string, string), long?>();
        foreach (var key in counterKeys)
        {
            var prev = await conn.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition("""
                    SELECT TOP 1 CAST(JSON_VALUE(PayloadJson, '$.value') AS bigint)
                    FROM telemetry.RawEvent
                    WHERE AssetId = @assetId
                      AND CollectorId = @collectorId
                      AND SourceId = @sourceId
                      AND EventType = 'Counter'
                      AND JSON_VALUE(PayloadJson, '$.name') = @name
                      AND EventTime < @minuteStart
                    ORDER BY EventTime DESC
                    """,
                    new { assetId, collectorId = key.CollectorId, sourceId = key.SourceId,
                          name = key.Name, minuteStart }, tx, cancellationToken: ct));
            prevCounterValues[key] = prev;
        }

        // Running state for counter delta calculation (ordered by EventTime, already sorted)
        var runningCounter = new Dictionary<(Guid, string, string), long>(
            prevCounterValues
                .Where(kv => kv.Value.HasValue)
                .ToDictionary(kv => kv.Key, kv => kv.Value!.Value));

        foreach (var row in rows)
        {
            using var doc = JsonDocument.Parse(row.PayloadJson);
            var payload = doc.RootElement;

            switch (row.EventType)
            {
                case "StateChange":
                    if (payload.TryGetProperty("state", out var stEl))
                        lastState = stEl.GetString();
                    break;

                case "Counter":
                {
                    var name = payload.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                    var value = payload.TryGetProperty("value", out var vEl) ? vEl.GetInt64() : 0L;
                    var reset = payload.TryGetProperty("reset", out var rEl) && rEl.GetBoolean();
                    var key = (row.CollectorId, row.SourceId, name);

                    long step;
                    if (reset)
                    {
                        step = value;
                    }
                    else if (runningCounter.TryGetValue(key, out var prev))
                    {
                        step = value - prev;
                    }
                    else
                    {
                        step = value;
                    }

                    if (step > 0) counterDelta += step;
                    runningCounter[key] = value;
                    break;
                }

                case "Fault":
                    if (payload.TryGetProperty("active", out var actEl) && actEl.GetBoolean())
                        faultCount++;
                    break;

                case "ProcessValue":
                    if (payload.TryGetProperty("value", out var pvEl) && pvEl.TryGetDouble(out var pv))
                    {
                        pvSum = (pvSum ?? 0) + pv;
                        pvMin = pvMin.HasValue ? Math.Min(pvMin.Value, pv) : pv;
                        pvMax = pvMax.HasValue ? Math.Max(pvMax.Value, pv) : pv;
                        pvCount++;
                    }
                    break;
            }
        }

        double? pvAvg = pvCount > 0 ? pvSum! / pvCount : null;

        bool windowClosed = now >= minuteStart.AddSeconds(60 + windowGraceSeconds);

        await conn.ExecuteAsync(new CommandDefinition("""
            MERGE telemetry.RollupMinute WITH (HOLDLOCK) AS t
            USING (SELECT @assetId AS AssetId, @minuteStart AS MinuteStart) AS s
                ON t.AssetId = s.AssetId AND t.MinuteStart = s.MinuteStart
            WHEN MATCHED THEN UPDATE SET
                EventCount = @eventCount,
                CounterDelta = @counterDelta,
                FaultEventCount = @faultCount,
                LastState = @lastState,
                ProcessValueAvg = @pvAvg,
                ProcessValueMin = @pvMin,
                ProcessValueMax = @pvMax,
                IsRestated = CASE WHEN @windowClosed = 1 THEN 1 ELSE t.IsRestated END,
                RestatementCount = CASE WHEN @windowClosed = 1 THEN t.RestatementCount + 1 ELSE t.RestatementCount END,
                LastComputedAt = @now
            WHEN NOT MATCHED THEN INSERT
                (TenantId, SiteId, LineId, AssetId, MinuteStart,
                 EventCount, CounterDelta, FaultEventCount, LastState,
                 ProcessValueAvg, ProcessValueMin, ProcessValueMax,
                 IsRestated, RestatementCount, FirstComputedAt, LastComputedAt)
            VALUES
                (@tenantId, @siteId, @lineId, @assetId, @minuteStart,
                 @eventCount, @counterDelta, @faultCount, @lastState,
                 @pvAvg, @pvMin, @pvMax,
                 @windowClosed, CASE WHEN @windowClosed = 1 THEN 1 ELSE 0 END,
                 @now, @now);
            """,
            new
            {
                assetId, minuteStart, tenantId, siteId, lineId,
                eventCount, counterDelta, faultCount, lastState,
                pvAvg, pvMin, pvMax,
                windowClosed,
                now
            }, tx, cancellationToken: ct));
    }
}
