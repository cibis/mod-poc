using System.Buffers;
using System.Text;
using System.Text.Json;
using Mod.Collector.Models;

namespace Mod.Collector.Edge;

internal static class CanonicalMapper
{
    // Factory helpers — sequence is provided by the caller (assigned atomically by the buffer).

    public static CanonicalEvent StateChange(string sourceId, long seq, string state)
    {
        var now = DateTimeOffset.UtcNow;
        return new CanonicalEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SourceId = sourceId,
            Sequence = seq,
            EventTime = now,
            ObservedTime = now,
            EventType = "StateChange",
            Payload = new StateChangePayload(state),
        };
    }

    public static CanonicalEvent Counter(string sourceId, long seq, string name, long value, bool reset)
    {
        var now = DateTimeOffset.UtcNow;
        return new CanonicalEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SourceId = sourceId,
            Sequence = seq,
            EventTime = now,
            ObservedTime = now,
            EventType = "Counter",
            Payload = new CounterPayload(name, value, reset),
        };
    }

    public static CanonicalEvent ProcessValue(string sourceId, long seq, string name, double value, string unit)
    {
        var now = DateTimeOffset.UtcNow;
        return new CanonicalEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SourceId = sourceId,
            Sequence = seq,
            EventTime = now,
            ObservedTime = now,
            EventType = "ProcessValue",
            Payload = new ProcessValuePayload(name, value, unit),
        };
    }

    public static CanonicalEvent Fault(string sourceId, long seq, string code, bool active)
    {
        var now = DateTimeOffset.UtcNow;
        return new CanonicalEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SourceId = sourceId,
            Sequence = seq,
            EventTime = now,
            ObservedTime = now,
            EventType = "Fault",
            Payload = new FaultPayload(code, active),
        };
    }

    // Serialises a CanonicalEvent to JSON per the contract (camelCase, UTC Z, millisecond precision).
    public static string ToJson(CanonicalEvent ev)
    {
        var buf = new ArrayBufferWriter<byte>();
        using var w = new Utf8JsonWriter(buf);
        w.WriteStartObject();
        w.WriteString("eventId", ev.EventId);
        w.WriteString("sourceId", ev.SourceId);
        w.WriteNumber("sequence", ev.Sequence);
        w.WriteString("eventTime", Ts(ev.EventTime));
        w.WriteString("observedTime", Ts(ev.ObservedTime));
        w.WriteString("eventType", ev.EventType);
        w.WriteString("quality", ev.Quality);
        w.WriteString("schemaVersion", ev.SchemaVersion);
        w.WritePropertyName("payload");
        w.WriteStartObject();
        switch (ev.Payload)
        {
            case StateChangePayload p:
                w.WriteString("state", p.State);
                break;
            case CounterPayload p:
                w.WriteString("name", p.Name);
                w.WriteNumber("value", p.Value);
                w.WriteBoolean("reset", p.Reset);
                break;
            case ProcessValuePayload p:
                w.WriteString("name", p.Name);
                w.WriteNumber("value", p.Value);
                w.WriteString("unit", p.Unit);
                break;
            case FaultPayload p:
                w.WriteString("code", p.Code);
                w.WriteBoolean("active", p.Active);
                break;
        }
        w.WriteEndObject();
        w.WriteEndObject();
        w.Flush();
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static string Ts(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}
