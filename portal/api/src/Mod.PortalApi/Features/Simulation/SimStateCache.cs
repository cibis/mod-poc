// DEMO SCAFFOLDING
using System.Collections.Concurrent;
using Mod.PortalApi.Features.Simulation.Models;

namespace Mod.PortalApi.Features.Simulation;

// Singleton in-memory state for the simulator. Holds latest collector statuses and provisioning
// states to avoid hitting SQL on every metrics tick or hub push.
internal sealed class SimStateCache
{
    private readonly ConcurrentDictionary<Guid, SimStatusBody> _statuses = new();
    private readonly ConcurrentDictionary<Guid, DateTime> _lastStatusAt = new();
    private readonly ConcurrentDictionary<Guid, bool> _bufferOverflowEver = new();
    private readonly ConcurrentDictionary<Guid, string> _provisioningStates = new();

    internal void UpdateStatus(Guid collectorId, SimStatusBody status, DateTime at)
    {
        _statuses[collectorId] = status;
        _lastStatusAt[collectorId] = at;
        if (status.BufferOverflowEver)
            _bufferOverflowEver[collectorId] = true;
    }

    internal SimStatusBody? GetStatus(Guid collectorId)
        => _statuses.TryGetValue(collectorId, out var s) ? s : null;

    internal DateTime? GetLastStatusAt(Guid collectorId)
        => _lastStatusAt.TryGetValue(collectorId, out var t) ? t : null;

    internal bool GetBufferOverflowEver(Guid collectorId)
        => _bufferOverflowEver.TryGetValue(collectorId, out var v) && v;

    internal IReadOnlyDictionary<Guid, SimStatusBody> GetAllStatuses()
        => _statuses;

    internal void SetProvisioningState(Guid collectorId, string state)
        => _provisioningStates[collectorId] = state;

    internal string GetProvisioningState(Guid collectorId)
        => _provisioningStates.TryGetValue(collectorId, out var s) ? s : "Off";

    // Called at startup to pre-populate provisioning states from SQL.
    internal void InitFromSql(IEnumerable<CollectorStateRow> rows)
    {
        foreach (var row in rows)
        {
            _provisioningStates[row.CollectorId] = row.ProvisioningState;
            if (row.BufferOverflowEver)
                _bufferOverflowEver[row.CollectorId] = true;
        }
    }
}
