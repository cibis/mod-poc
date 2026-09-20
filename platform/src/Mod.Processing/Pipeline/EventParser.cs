using System.Text.Json;
using Mod.Platform.Common.Models;

namespace Mod.Processing.Pipeline;

internal static class EventParser
{
    /// <summary>
    /// Parses and validates one event element from the raw batch JSON.
    /// Returns (event, null, null) on success or (null, reasonCode, detail) on failure.
    /// Validation order matches the dead-letter contract.
    /// </summary>
    internal static (CanonicalEvent? Event, string? ReasonCode, string? Detail) Parse(
        JsonElement el, DateTimeOffset receivedAt)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return (null, "SchemaInvalid", "Event must be a JSON object");

        // schemaVersion → UnsupportedSchemaVersion before any other schema checks
        if (!el.TryGetProperty("schemaVersion", out var svEl) || svEl.ValueKind != JsonValueKind.String)
            return (null, "SchemaInvalid", "Missing schemaVersion");

        var schemaVersion = svEl.GetString()!;
        if (!schemaVersion.StartsWith("1.", StringComparison.Ordinal))
            return (null, "UnsupportedSchemaVersion", $"schemaVersion={schemaVersion}");

        // eventType
        if (!el.TryGetProperty("eventType", out var etEl) || etEl.ValueKind != JsonValueKind.String)
            return (null, "SchemaInvalid", "Missing eventType");
        if (!Enum.TryParse<EventType>(etEl.GetString(), out var eventType))
            return (null, "UnknownEventType", $"eventType={etEl.GetString()}");

        // eventId
        if (!el.TryGetProperty("eventId", out var eidEl) || eidEl.ValueKind != JsonValueKind.String
            || !Guid.TryParse(eidEl.GetString(), out var eventId))
            return (null, "SchemaInvalid", "Missing or invalid eventId");

        // sourceId
        if (!el.TryGetProperty("sourceId", out var sidEl) || sidEl.ValueKind != JsonValueKind.String)
            return (null, "SchemaInvalid", "Missing sourceId");
        var sourceId = sidEl.GetString()!;
        if (sourceId.Length == 0 || sourceId.Length > 100)
            return (null, "SchemaInvalid", "sourceId length invalid");

        // sequence
        if (!el.TryGetProperty("sequence", out var seqEl) || seqEl.ValueKind != JsonValueKind.Number
            || !seqEl.TryGetInt64(out var sequence) || sequence < 1)
            return (null, "SchemaInvalid", "Missing or invalid sequence");

        // eventTime
        if (!el.TryGetProperty("eventTime", out var etimeEl) || etimeEl.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(etimeEl.GetString(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var eventTime))
            return (null, "SchemaInvalid", "Missing or invalid eventTime");

        // observedTime
        if (!el.TryGetProperty("observedTime", out var otEl) || otEl.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(otEl.GetString(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var observedTime))
            return (null, "SchemaInvalid", "Missing or invalid observedTime");

        // quality
        if (!el.TryGetProperty("quality", out var qEl) || qEl.ValueKind != JsonValueKind.String
            || !Enum.TryParse<Quality>(qEl.GetString(), out var quality))
            return (null, "SchemaInvalid", "Missing or invalid quality");

        // payload
        if (!el.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return (null, "SchemaInvalid", "Missing payload");

        // TimestampOutOfRange
        if (eventTime > receivedAt.AddMinutes(5))
            return (null, "TimestampOutOfRange", $"eventTime {eventTime:O} is in the future");
        if (eventTime < receivedAt.AddDays(-30))
            return (null, "TimestampOutOfRange", $"eventTime {eventTime:O} is older than 30 days");

        // ValueOutOfRange (ProcessValue only)
        if (eventType == EventType.ProcessValue)
        {
            if (!payload.TryGetProperty("value", out var valEl)
                || valEl.ValueKind != JsonValueKind.Number
                || !valEl.TryGetDouble(out var pv)
                || !double.IsFinite(pv)
                || Math.Abs(pv) > 1e9)
                return (null, "ValueOutOfRange", "ProcessValue.value is non-finite or out of range");
        }

        return (new CanonicalEvent(eventId, sourceId, sequence, eventTime, observedTime,
            eventType, payload, quality, schemaVersion), null, null);
    }
}
