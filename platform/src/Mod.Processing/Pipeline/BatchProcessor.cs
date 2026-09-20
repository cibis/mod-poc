using System.IO.Compression;
using System.Text.Json;
using Azure.Messaging.EventHubs;
using Dapper;
using Mod.Platform.Common.EventHubs;
using Mod.Platform.Common.Models;
using Mod.Platform.Common.Sql;

namespace Mod.Processing.Pipeline;

internal record MessageContext(
    Guid CollectorId,
    Guid TenantId,
    Guid SiteId,
    Guid BatchId,
    DateTimeOffset ReceivedAt);

internal record AttributedEvent(CanonicalEvent Event, AssetMapping Mapping);

internal record DeadLetterEntry(
    Guid? EventId,
    Guid? TenantId,
    Guid? SiteId,
    string ReasonCode,
    string? ReasonDetail,
    string EventJson,
    DateTimeOffset ReceivedAt);

internal record BatchWriteContext(
    MessageContext Message,
    IReadOnlyList<AttributedEvent> Valid,
    IReadOnlyList<DeadLetterEntry> DeadLetters);

internal sealed class BatchProcessor(
    MappingCache mappingCache,
    SqlWriter sqlWriter,
    SqlConnectionFactory sqlFactory,
    ProcessingOptions options,
    ILogger<BatchProcessor> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task ProcessAsync(EventData data, CancellationToken ct)
    {
        var ctx = ExtractContext(data);
        if (ctx is null)
        {
            logger.LogWarning("Event Hubs message missing required properties; skipping");
            return;
        }

        // 1. Decompress + parse body
        List<JsonElement> rawEvents;
        string rawBodyBase64;
        try
        {
            var bodyBytes = data.Body.ToArray();
            rawBodyBase64 = Convert.ToBase64String(bodyBytes);
            using var ms = new MemoryStream();
            await using (var gz = new GZipStream(new MemoryStream(bodyBytes), CompressionMode.Decompress))
                await gz.CopyToAsync(ms, ct);
            using var doc = JsonDocument.Parse(ms.ToArray());
            var root = doc.RootElement;
            rawEvents = new List<JsonElement>();
            if (root.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in eventsEl.EnumerateArray())
                    rawEvents.Add(el.Clone());
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "BatchUnreadable collectorId={CollectorId} batchId={BatchId}",
                ctx.CollectorId, ctx.BatchId);
            var dl = new DeadLetterEntry(null, ctx.TenantId, ctx.SiteId, "BatchUnreadable",
                ex.Message, data.Body.ToArray().Length > 0
                    ? Convert.ToBase64String(data.Body.ToArray()) : string.Empty,
                ctx.ReceivedAt);
            var emptyCtx = new BatchWriteContext(ctx, [], [dl]);
            await sqlWriter.WriteAsync(emptyCtx, ct);
            return;
        }

        // 2. Per-event schema validation
        var parsed = new List<(CanonicalEvent Event, string RawJson)>();
        var deadLetters = new List<DeadLetterEntry>();

        foreach (var el in rawEvents)
        {
            var rawJson = el.GetRawText();
            var (evt, code, detail) = EventParser.Parse(el, ctx.ReceivedAt);
            if (evt is null)
            {
                Guid? eid = el.TryGetProperty("eventId", out var eidEl) && Guid.TryParse(eidEl.GetString(), out var g) ? g : null;
                deadLetters.Add(new DeadLetterEntry(eid, ctx.TenantId, ctx.SiteId, code!, detail, rawJson, ctx.ReceivedAt));
            }
            else
            {
                parsed.Add((evt, rawJson));
            }
        }

        // 3. De-duplicate
        var parsedIds = parsed.Select(p => p.Event.EventId).ToList();
        HashSet<Guid> alreadyProcessed = new();
        if (parsedIds.Count > 0)
        {
            await using var conn = sqlFactory.CreateConnection();
            var existing = await conn.QueryAsync<Guid>(
                new CommandDefinition(
                    "SELECT EventId FROM telemetry.ProcessedEvent WHERE EventId IN @ids",
                    new { ids = parsedIds }, cancellationToken: ct));
            foreach (var id in existing) alreadyProcessed.Add(id);
        }

        var deduped = parsed.Where(p => !alreadyProcessed.Contains(p.Event.EventId)).ToList();

        // 4. Attribute + 5. Counter check
        var batchCounterState = new Dictionary<(Guid, string, string), long>();
        var valid = new List<AttributedEvent>();

        foreach (var (evt, rawJson) in deduped)
        {
            // Attribution
            var mapping = await mappingCache.GetAsync(ctx.CollectorId, evt.SourceId, ct);
            if (mapping is null)
            {
                deadLetters.Add(new DeadLetterEntry(evt.EventId, ctx.TenantId, ctx.SiteId,
                    "UnmappedSource", null, rawJson, ctx.ReceivedAt));
                continue;
            }
            if (mapping.TenantId != ctx.TenantId)
            {
                deadLetters.Add(new DeadLetterEntry(evt.EventId, ctx.TenantId, ctx.SiteId,
                    "UnmappedSource", "TenantMismatch", rawJson, ctx.ReceivedAt));
                continue;
            }

            // Counter check
            if (evt.EventType == EventType.Counter)
            {
                var name = evt.Payload.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                var value = evt.Payload.TryGetProperty("value", out var vEl) ? vEl.GetInt64() : 0L;
                var reset = evt.Payload.TryGetProperty("reset", out var rEl) && rEl.GetBoolean();
                var counterKey = (ctx.CollectorId, evt.SourceId, name);

                long? prevValue = null;
                if (batchCounterState.TryGetValue(counterKey, out var bv))
                    prevValue = bv;
                else
                {
                    prevValue = await GetLastCounterValueAsync(ctx.CollectorId, evt.SourceId, name, ct);
                    if (prevValue.HasValue)
                        batchCounterState[counterKey] = prevValue.Value;
                }

                if (prevValue.HasValue && value < prevValue.Value && !reset)
                {
                    deadLetters.Add(new DeadLetterEntry(evt.EventId, ctx.TenantId, ctx.SiteId,
                        "CounterDecreaseWithoutReset",
                        $"value={value} prev={prevValue.Value}", rawJson, ctx.ReceivedAt));
                    continue;
                }

                batchCounterState[counterKey] = value;
            }

            valid.Add(new AttributedEvent(evt, mapping));
        }

        // 6. Write
        var writeCtx = new BatchWriteContext(ctx, valid, deadLetters);
        await sqlWriter.WriteAsync(writeCtx, ct);

        // 7. Demo knob — artificial delay so backlog scaling is visible at small fleet sizes
        if (options.ProcessingDelayMs > 0)
            await Task.Delay(options.ProcessingDelayMs, ct);
    }

    private static MessageContext? ExtractContext(EventData data)
    {
        var props = data.Properties;
        if (!props.TryGetValue(EventHubMessageProperties.CollectorId, out var cidObj)
            || !Guid.TryParse(cidObj?.ToString(), out var collectorId))
            return null;
        if (!props.TryGetValue(EventHubMessageProperties.TenantId, out var tidObj)
            || !Guid.TryParse(tidObj?.ToString(), out var tenantId))
            return null;
        if (!props.TryGetValue(EventHubMessageProperties.SiteId, out var sidObj)
            || !Guid.TryParse(sidObj?.ToString(), out var siteId))
            return null;
        if (!props.TryGetValue(EventHubMessageProperties.BatchId, out var bidObj)
            || !Guid.TryParse(bidObj?.ToString(), out var batchId))
            return null;
        if (!props.TryGetValue(EventHubMessageProperties.ReceivedAt, out var raObj)
            || !DateTimeOffset.TryParse(raObj?.ToString(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var receivedAt))
            return null;

        return new MessageContext(collectorId, tenantId, siteId, batchId, receivedAt);
    }

    private async Task<long?> GetLastCounterValueAsync(
        Guid collectorId, string sourceId, string name, CancellationToken ct)
    {
        const string sql = """
            SELECT TOP 1 CAST(JSON_VALUE(PayloadJson, '$.value') AS bigint)
            FROM telemetry.RawEvent
            WHERE CollectorId = @collectorId
              AND SourceId = @sourceId
              AND EventType = 'Counter'
              AND JSON_VALUE(PayloadJson, '$.name') = @name
            ORDER BY EventTime DESC
            """;
        await using var conn = sqlFactory.CreateConnection();
        var result = await conn.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(sql, new { collectorId, sourceId, name }, cancellationToken: ct));
        return result;
    }
}
