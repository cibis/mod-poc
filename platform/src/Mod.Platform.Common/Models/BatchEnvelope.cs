namespace Mod.Platform.Common.Models;

public record BatchEnvelope(
    Guid BatchId,
    Guid CollectorId,
    string SchemaVersion,
    DateTimeOffset SentAt,
    IReadOnlyList<CanonicalEvent> Events);
