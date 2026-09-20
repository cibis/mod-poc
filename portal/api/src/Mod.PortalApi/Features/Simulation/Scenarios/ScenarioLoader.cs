// DEMO SCAFFOLDING
namespace Mod.PortalApi.Features.Simulation.Scenarios;

// Provides the static scenario definitions as in-code data (embedded spec: JSON files).
internal static class ScenarioLoader
{
    private static readonly IReadOnlyList<ScenarioDefinition> _all = BuildAll();

    internal static IReadOnlyList<ScenarioDefinition> All => _all;

    internal static ScenarioDefinition? Find(string name)
        => _all.FirstOrDefault(s => s.Name == name);

    private static IReadOnlyList<ScenarioDefinition> BuildAll() =>
    [
        new ScenarioDefinition(
            Name: "steady-state",
            Description: "Power on all registered collectors and run at rate 1 with link up.",
            DurationSeconds: 600,
            Parameters:
            [
                new ScenarioParameterDef("tenantId", "guid", null),
            ],
            Steps:
            [
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(All: true),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "up" }),
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(All: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 1.0 }),
            ]),

        new ScenarioDefinition(
            Name: "one-site-offline",
            Description: "Take one collector's link down for 5 minutes then restore.",
            DurationSeconds: 600,
            Parameters:
            [
                new ScenarioParameterDef("collectorId", "guid", null), // default resolved by name at runtime
            ],
            Steps:
            [
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(All: true),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "up" }),
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(All: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 1.0 }),
                new ScenarioStep(60, "send-command",
                    new StepTargetSpec(CollectorParamName: "collectorId"),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "down" }),
                new ScenarioStep(360, "send-command",
                    new StepTargetSpec(CollectorParamName: "collectorId"),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "up" }),
            ]),

        new ScenarioDefinition(
            Name: "regional-outage",
            Description: "Take an entire region offline for 10 minutes then restore.",
            DurationSeconds: 1200,
            Parameters:
            [
                new ScenarioParameterDef("regionLabel", "string", "EU-North"),
            ],
            Steps:
            [
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(All: true),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "up" }),
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(All: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 1.0 }),
                new ScenarioStep(60, "send-command",
                    new StepTargetSpec(RegionLabelParamName: "regionLabel"),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "down" }),
                new ScenarioStep(660, "send-command",
                    new StepTargetSpec(All: true),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "up" }),
            ]),

        new ScenarioDefinition(
            Name: "rate-ramp",
            Description: "Ramp all collectors through rates 1→5→10→20 then back to 1.",
            DurationSeconds: 600,
            Parameters: [],
            Steps:
            [
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(AllPowered: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 1.0 }),
                new ScenarioStep(120, "send-command",
                    new StepTargetSpec(AllPowered: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 5.0 }),
                new ScenarioStep(240, "send-command",
                    new StepTargetSpec(AllPowered: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 10.0 }),
                new ScenarioStep(360, "send-command",
                    new StepTargetSpec(AllPowered: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 20.0 }),
                new ScenarioStep(480, "send-command",
                    new StepTargetSpec(AllPowered: true),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 1.0 }),
            ]),

        new ScenarioDefinition(
            Name: "bad-data",
            Description: "Inject invalid data share for 5 minutes then clear.",
            DurationSeconds: 600,
            Parameters:
            [
                new ScenarioParameterDef("share", "number", 0.1),
            ],
            Steps:
            [
                new ScenarioStep(30, "send-command",
                    new StepTargetSpec(AllPowered: true),
                    CommandType: "SetInvalidShare",
                    CommandArgs: new Dictionary<string, object?> { ["share"] = 0.1 }), // resolved at runtime from params
                new ScenarioStep(330, "send-command",
                    new StepTargetSpec(AllPowered: true),
                    CommandType: "SetInvalidShare",
                    CommandArgs: new Dictionary<string, object?> { ["share"] = 0.0 }),
            ]),

        new ScenarioDefinition(
            Name: "buffer-overflow",
            Description: "Fill buffer to capacity with link down then drain.",
            DurationSeconds: 600,
            Parameters:
            [
                new ScenarioParameterDef("collectorId", "guid", null),
            ],
            Steps:
            [
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(CollectorParamName: "collectorId"),
                    CommandType: "SetBufferCapacity",
                    CommandArgs: new Dictionary<string, object?> { ["capacityEvents"] = 2000 }),
                new ScenarioStep(0, "send-command",
                    new StepTargetSpec(CollectorParamName: "collectorId"),
                    CommandType: "SetRate",
                    CommandArgs: new Dictionary<string, object?> { ["eventsPerSecondPerSource"] = 10.0 }),
                new ScenarioStep(30, "send-command",
                    new StepTargetSpec(CollectorParamName: "collectorId"),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "down" }),
                new ScenarioStep(330, "send-command",
                    new StepTargetSpec(CollectorParamName: "collectorId"),
                    CommandType: "SetLink",
                    CommandArgs: new Dictionary<string, object?> { ["state"] = "up" }),
                new ScenarioStep(330, "send-command",
                    new StepTargetSpec(CollectorParamName: "collectorId"),
                    CommandType: "SetBufferCapacity",
                    CommandArgs: new Dictionary<string, object?> { ["capacityEvents"] = 200000 }),
            ]),
    ];
}
