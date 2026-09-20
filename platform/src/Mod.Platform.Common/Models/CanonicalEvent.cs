using System.Text.Json;

namespace Mod.Platform.Common.Models;

public record CanonicalEvent(
    Guid EventId,
    string SourceId,
    long Sequence,
    DateTimeOffset EventTime,
    DateTimeOffset ObservedTime,
    EventType EventType,
    JsonElement Payload,
    Quality Quality,
    string SchemaVersion);

public enum EventType { StateChange, Counter, ProcessValue, Fault }

public enum Quality { Good, Uncertain, Bad }
