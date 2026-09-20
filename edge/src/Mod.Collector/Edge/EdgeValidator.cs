using System.Diagnostics.CodeAnalysis;
using Mod.Collector.Models;

namespace Mod.Collector.Edge;

// Schema-only validation at the edge.  Semantic constraints (timestamp range, counter
// monotonicity, etc.) are checked by the cloud; the invalid data injector deliberately
// produces events that pass here but fail there.
internal sealed class EdgeValidator
{
    private static readonly HashSet<string> ValidEventTypes = ["StateChange", "Counter", "ProcessValue", "Fault"];
    private static readonly HashSet<string> ValidQualities = ["Good", "Uncertain", "Bad"];
    private static readonly HashSet<string> ValidStates = ["Running", "Idle", "Stopped", "Fault", "Setup"];

    public bool IsValid(CanonicalEvent ev, [NotNullWhen(false)] out string? reason)
    {
        if (string.IsNullOrEmpty(ev.EventId))
        {
            reason = "missing eventId";
            return false;
        }
        if (string.IsNullOrEmpty(ev.SourceId))
        {
            reason = "missing sourceId";
            return false;
        }
        if (!ValidEventTypes.Contains(ev.EventType))
        {
            reason = $"unknown eventType '{ev.EventType}'";
            return false;
        }
        if (!ValidQualities.Contains(ev.Quality))
        {
            reason = $"unknown quality '{ev.Quality}'";
            return false;
        }
        if (!ev.SchemaVersion.StartsWith("1.", StringComparison.Ordinal))
        {
            reason = $"unsupported schemaVersion '{ev.SchemaVersion}'";
            return false;
        }

        reason = ev.EventType switch
        {
            "StateChange" when ev.Payload is StateChangePayload { State: var s } && !ValidStates.Contains(s)
                => $"invalid state '{s}'",
            "StateChange" when ev.Payload is not StateChangePayload
                => "payload type mismatch for StateChange",
            "Counter" when ev.Payload is CounterPayload { Value: < 0 }
                => "counter value < 0",
            "Counter" when ev.Payload is not CounterPayload
                => "payload type mismatch for Counter",
            "ProcessValue" when ev.Payload is ProcessValuePayload { Value: var v } && !double.IsFinite(v)
                => "processValue not finite",
            "ProcessValue" when ev.Payload is not ProcessValuePayload
                => "payload type mismatch for ProcessValue",
            "Fault" when ev.Payload is not FaultPayload
                => "payload type mismatch for Fault",
            _ => null,
        };

        return reason is null;
    }
}
