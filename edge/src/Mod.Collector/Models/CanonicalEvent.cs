namespace Mod.Collector.Models;

internal sealed record CanonicalEvent
{
    public required string EventId { get; init; }
    public required string SourceId { get; init; }
    public required long Sequence { get; init; }
    public required DateTimeOffset EventTime { get; init; }
    public required DateTimeOffset ObservedTime { get; init; }
    public required string EventType { get; init; }
    public required object Payload { get; init; }
    public string Quality { get; init; } = "Good";
    public string SchemaVersion { get; init; } = "1.0";
}

internal sealed record StateChangePayload(string State);
internal sealed record CounterPayload(string Name, long Value, bool Reset);
internal sealed record ProcessValuePayload(string Name, double Value, string Unit);
internal sealed record FaultPayload(string Code, bool Active);
