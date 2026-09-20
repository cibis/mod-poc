using System.Text.Json;

namespace Mod.IngestApi.Validation;

public static class EnvelopeValidator
{
    private static readonly HashSet<string> ValidEventTypes =
        ["StateChange", "Counter", "ProcessValue", "Fault"];

    private static readonly HashSet<string> ValidQualities =
        ["Good", "Uncertain", "Bad"];

    public record Result(
        Guid BatchId,
        int EventCount,
        string? ErrorCode,
        IReadOnlyList<string> Errors)
    {
        public bool IsSuccess => ErrorCode is null;
    }

    public static Result Validate(byte[] json, Guid certCollectorId, int maxEvents)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json.AsMemory());
        }
        catch (JsonException ex)
        {
            return new(Guid.Empty, 0, "EnvelopeInvalid", [$"JSON parse error: {ex.Message}"]);
        }

        using (doc)
        {
            return ValidateRoot(doc.RootElement, certCollectorId, maxEvents);
        }
    }

    private static Result ValidateRoot(JsonElement root, Guid certCollectorId, int maxEvents)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return new(Guid.Empty, 0, "EnvelopeInvalid", ["Envelope root must be a JSON object"]);

        var errors = new List<string>(20);

        // schemaVersion — distinct error code if wrong version
        if (root.TryGetProperty("schemaVersion", out var svEl))
        {
            if (svEl.ValueKind != JsonValueKind.String)
                AddError(errors, "schemaVersion must be a string");
            else if (svEl.GetString() != "1.0")
                return new(Guid.Empty, 0, "UnsupportedSchemaVersion", []);
        }
        else
        {
            AddError(errors, "schemaVersion is required");
        }

        // collectorId — distinct error code on mismatch
        if (root.TryGetProperty("collectorId", out var cidEl))
        {
            if (cidEl.ValueKind != JsonValueKind.String)
                AddError(errors, "collectorId must be a string");
            else if (!Guid.TryParse(cidEl.GetString(), out var envelopeCid))
                AddError(errors, "collectorId is not a valid GUID");
            else if (envelopeCid != certCollectorId)
                return new(Guid.Empty, 0, "CollectorMismatch", []);
        }
        else
        {
            AddError(errors, "collectorId is required");
        }

        // batchId
        var batchId = Guid.Empty;
        if (root.TryGetProperty("batchId", out var bidEl))
        {
            if (bidEl.ValueKind != JsonValueKind.String)
                AddError(errors, "batchId must be a string");
            else if (!Guid.TryParse(bidEl.GetString(), out batchId))
                AddError(errors, "batchId is not a valid GUID");
        }
        else
        {
            AddError(errors, "batchId is required");
        }

        // sentAt
        if (!root.TryGetProperty("sentAt", out var satEl))
            AddError(errors, "sentAt is required");
        else if (satEl.ValueKind != JsonValueKind.String)
            AddError(errors, "sentAt must be a string");

        // events
        var eventCount = 0;
        if (root.TryGetProperty("events", out var eventsEl))
        {
            if (eventsEl.ValueKind != JsonValueKind.Array)
            {
                AddError(errors, "events must be an array");
            }
            else
            {
                eventCount = eventsEl.GetArrayLength();
                if (eventCount < 1)
                    AddError(errors, "events must contain at least 1 event");
                else if (eventCount > maxEvents)
                    AddError(errors, $"events count {eventCount} exceeds limit of {maxEvents}");
                else
                    ValidateEvents(eventsEl, errors);
            }
        }
        else
        {
            AddError(errors, "events is required");
        }

        return errors.Count > 0
            ? new(batchId, eventCount, "EnvelopeInvalid", errors)
            : new(batchId, eventCount, null, []);
    }

    private static void ValidateEvents(JsonElement events, List<string> errors)
    {
        var i = 0;
        foreach (var evt in events.EnumerateArray())
        {
            if (errors.Count >= 20) return;
            ValidateEvent(evt, i, errors);
            i++;
        }
    }

    private static void ValidateEvent(JsonElement evt, int index, List<string> errors)
    {
        if (evt.ValueKind != JsonValueKind.Object)
        {
            AddError(errors, $"events[{index}] must be an object");
            return;
        }

        CheckGuid(evt, "eventId", index, errors);
        CheckString(evt, "sourceId", index, errors);
        CheckInt64(evt, "sequence", index, errors);
        CheckString(evt, "eventTime", index, errors);
        CheckString(evt, "observedTime", index, errors);
        CheckEnum(evt, "eventType", ValidEventTypes, index, errors);
        CheckObject(evt, "payload", index, errors);
        CheckEnum(evt, "quality", ValidQualities, index, errors);
        CheckString(evt, "schemaVersion", index, errors);
    }

    private static void CheckString(JsonElement obj, string name, int i, List<string> errors)
    {
        if (errors.Count >= 20) return;
        if (!obj.TryGetProperty(name, out var el))
            AddError(errors, $"events[{i}].{name} is required");
        else if (el.ValueKind != JsonValueKind.String)
            AddError(errors, $"events[{i}].{name} must be a string");
    }

    private static void CheckGuid(JsonElement obj, string name, int i, List<string> errors)
    {
        if (errors.Count >= 20) return;
        if (!obj.TryGetProperty(name, out var el))
        { AddError(errors, $"events[{i}].{name} is required"); return; }
        if (el.ValueKind != JsonValueKind.String || !Guid.TryParse(el.GetString(), out _))
            AddError(errors, $"events[{i}].{name} must be a GUID string");
    }

    private static void CheckInt64(JsonElement obj, string name, int i, List<string> errors)
    {
        if (errors.Count >= 20) return;
        if (!obj.TryGetProperty(name, out var el))
        { AddError(errors, $"events[{i}].{name} is required"); return; }
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt64(out var v) || v < 1)
            AddError(errors, $"events[{i}].{name} must be an integer >= 1");
    }

    private static void CheckObject(JsonElement obj, string name, int i, List<string> errors)
    {
        if (errors.Count >= 20) return;
        if (!obj.TryGetProperty(name, out var el))
            AddError(errors, $"events[{i}].{name} is required");
        else if (el.ValueKind != JsonValueKind.Object)
            AddError(errors, $"events[{i}].{name} must be an object");
    }

    private static void CheckEnum(JsonElement obj, string name, HashSet<string> valid, int i, List<string> errors)
    {
        if (errors.Count >= 20) return;
        if (!obj.TryGetProperty(name, out var el))
        { AddError(errors, $"events[{i}].{name} is required"); return; }
        var s = el.ValueKind == JsonValueKind.String ? el.GetString() : null;
        if (s is null || !valid.Contains(s))
            AddError(errors, $"events[{i}].{name} must be one of: {string.Join(", ", valid)}");
    }

    private static void AddError(List<string> errors, string message)
    {
        if (errors.Count < 20)
            errors.Add(message);
    }
}
