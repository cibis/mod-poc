// DEMO SCAFFOLDING
namespace Mod.PortalApi.Features.Simulation.Scenarios;

internal sealed record ScenarioDefinition(
    string Name,
    string Description,
    int DurationSeconds,
    IReadOnlyList<ScenarioParameterDef> Parameters,
    IReadOnlyList<ScenarioStep> Steps);

internal sealed record ScenarioParameterDef(
    string Name,
    string Type,
    object? Default);

// Resolved target at step execution time
internal sealed record StepTargetSpec(
    // Parameter name holding collectorId(s) value - resolved from scenario parameters
    string? CollectorParamName = null,
    // Literal region label
    string? RegionLabel = null,
    // Parameter name holding regionLabel - resolved from scenario parameters
    string? RegionLabelParamName = null,
    // All registered collectors
    bool? All = null,
    // All currently powered-on collectors
    bool? AllPowered = null);

internal sealed record ScenarioStep(
    int AtSeconds,
    // "power" | "send-command"
    string Action,
    StepTargetSpec? Targets,
    // Command type for send-command action (SetLink, SetRate, SetPaused, SetBufferCapacity, SetInvalidShare)
    string? CommandType = null,
    // Command args dictionary for send-command action
    IReadOnlyDictionary<string, object?>? CommandArgs = null,
    // Power on (true) or off (false) for power action
    bool? Power = null);
