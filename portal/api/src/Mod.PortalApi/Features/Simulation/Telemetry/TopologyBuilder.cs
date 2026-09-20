// DEMO SCAFFOLDING
using Mod.PortalApi.Features.Simulation.Models;

namespace Mod.PortalApi.Features.Simulation.Telemetry;

// Builds the topology snapshot from current in-memory and metric state.
internal static class TopologyBuilder
{
    internal static TopologySnapshot Build(
        IReadOnlyList<CollectorStateRow> collectorStates,
        IReadOnlyDictionary<Guid, SimStatusBody> statuses,
        IReadOnlyDictionary<string, int> replicaCounts)
    {
        var nodes = new List<TopologyNode>();
        var edges = new List<TopologyEdge>();

        // Platform nodes (production space)
        nodes.Add(PlatformNode("ingest", "ingest", "Ingest API", replicaCounts));
        nodes.Add(PlatformNode("processing", "processing", "Processing", replicaCounts));
        nodes.Add(PlatformNode("management", "management", "Management API", replicaCounts));
        nodes.Add(PlatformNode("portal", "portal", "Admin Portal", replicaCounts));
        nodes.Add(PlatformNode("reporting", "reporting", "Reporting API", replicaCounts));
        nodes.Add(new TopologyNode("eventhub", "eventHub", "Event Hub", "production",
            NodeState("ok"), null, null, null, new()));
        nodes.Add(new TopologyNode("sql", "sql", "SQL Database", "production",
            NodeState("ok"), null, null, null, new()));
        nodes.Add(new TopologyNode("cmdBus", "cmdBus", "Command Bus", "production",
            NodeState("ok"), null, null, null, new()));
        nodes.Add(new TopologyNode("simBus", "simBus", "Simulator Bus", "outside",
            NodeState("ok"), null, null, null, new()));

        // Platform edges
        edges.Add(new TopologyEdge("ingest-eventhub", "ingest", "eventhub", "data", true, null));
        edges.Add(new TopologyEdge("eventhub-processing", "eventhub", "processing", "data", true, null));
        edges.Add(new TopologyEdge("processing-sql", "processing", "sql", "data", true, null));
        edges.Add(new TopologyEdge("management-sql", "management", "sql", "management", true, null));
        edges.Add(new TopologyEdge("reporting-sql", "reporting", "sql", "data", true, null));
        edges.Add(new TopologyEdge("portal-sql", "portal", "sql", "management", true, null));
        edges.Add(new TopologyEdge("portal-cmdBus", "portal", "cmdBus", "command", true, null));
        edges.Add(new TopologyEdge("portal-simBus", "portal", "simBus", "simulation", true, null));

        // Collector nodes + edges (controller space)
        foreach (var state in collectorStates)
        {
            var poweredOn = state.ProvisioningState is "Running" or "Provisioning";
            var status = statuses.TryGetValue(state.CollectorId, out var s) ? s : null;
            var linkUp = status?.LinkState == "up";
            var enrolled = status?.Enrolled == true;
            var nodeState = state.ProvisioningState switch
            {
                "Running" when linkUp => "ok",
                "Running" => "down",
                "Provisioning" => "scaling",
                "Failed" => "down",
                _ => "off",
            };

            var metrics = new Dictionary<string, double>();
            if (status is not null)
            {
                metrics["bufferDepth"] = status.BufferDepthEvents;
                metrics["bufferCapacity"] = status.BufferCapacityEvents;
            }

            nodes.Add(new TopologyNode(
                state.CollectorId.ToString("D"),
                "collector",
                state.CollectorId.ToString("D")[..8] + "…",
                "controller",
                nodeState,
                poweredOn ? 1 : 0,
                null, null,
                metrics));

            var collId = state.CollectorId.ToString("D");
            edges.Add(new TopologyEdge(
                $"{collId}-ingest", collId, "ingest", "data",
                poweredOn && linkUp && enrolled, null));
            edges.Add(new TopologyEdge(
                $"{collId}-management", collId, "management", "management",
                poweredOn && linkUp && enrolled, null));
            edges.Add(new TopologyEdge(
                $"{collId}-cmdBus", collId, "cmdBus", "command",
                poweredOn && linkUp, null));
            edges.Add(new TopologyEdge(
                $"{collId}-simBus", collId, "simBus", "simulation",
                poweredOn, null));
        }

        return new TopologySnapshot(nodes, edges);
    }

    private static TopologyNode PlatformNode(
        string id, string appKey, string label,
        IReadOnlyDictionary<string, int> replicaCounts)
    {
        var replicas = replicaCounts.TryGetValue(appKey, out var r) ? r : (int?)null;
        var state = replicas switch
        {
            null => "ok",
            0 => "down",
            _ => "ok",
        };
        return new TopologyNode(id, id, label, "production", state,
            replicas, null, null, new());
    }

    private static string NodeState(string s) => s;
}

internal sealed record TopologySnapshot(
    IReadOnlyList<TopologyNode> Nodes,
    IReadOnlyList<TopologyEdge> Edges);

internal sealed record TopologyNode(
    string Id,
    string Kind,
    string Label,
    string Space,
    string State,
    int? Replicas,
    int? MinReplicas,
    int? MaxReplicas,
    Dictionary<string, double> Metrics);

internal sealed record TopologyEdge(
    string Id,
    string From,
    string To,
    string Kind,
    bool Active,
    string? Label);
