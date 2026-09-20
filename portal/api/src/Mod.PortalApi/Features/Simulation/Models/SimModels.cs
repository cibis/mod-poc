// DEMO SCAFFOLDING
namespace Mod.PortalApi.Features.Simulation.Models;

internal sealed record TargetSelector(
    IReadOnlyList<Guid>? CollectorIds,
    IReadOnlyList<Guid>? SiteIds,
    Guid? TenantId,
    string? RegionLabel,
    bool? All);

internal sealed class CollectorStateRow
{
    public Guid CollectorId { get; set; }
    public bool PoweredOn { get; set; }
    public string? ContainerAppName { get; set; }
    public string ProvisioningState { get; set; } = "Off";
    public string? LastError { get; set; }
    public bool BufferOverflowEver { get; set; }
    public string? LastStatusJson { get; set; }
    public DateTime? LastStatusAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

internal sealed record SimStatusBody(
    Guid CollectorId,
    DateTime SentAt,
    string? SoftwareVersion,
    bool Enrolled,
    bool AuthRejected,
    string LinkState,
    bool Paused,
    double EventsPerSecondPerSource,
    double InvalidShare,
    int BufferDepthEvents,
    int BufferCapacityEvents,
    bool BufferOverflowEver,
    long OverflowDroppedTotal,
    long ProducedTotal,
    long AckedTotal,
    DateTime? LastAckAt,
    Guid? LastAppliedCommandId);

internal sealed record SimCommandBody(
    Guid CommandId,
    string Type,
    DateTime IssuedAt,
    IReadOnlyDictionary<string, object?>? Args);

internal sealed record TimelineMarkerRow(
    long MarkerId,
    DateTime At,
    string Kind,
    string Label,
    string? TargetsJson,
    Guid? ScenarioRunId);

internal sealed record ScenarioRunRow(
    Guid ScenarioRunId,
    string ScenarioName,
    string ParametersJson,
    DateTime StartedAt,
    DateTime? EndedAt,
    string Status,
    string StartedBy);
