using System.Collections.Concurrent;

namespace Mod.ReportingApi.Live;

public sealed class ConnectedTenantTracker
{
    private readonly ConcurrentDictionary<Guid, int> _counts = new();

    public void Add(Guid tenantId) =>
        _counts.AddOrUpdate(tenantId, 1, (_, c) => c + 1);

    public void Remove(Guid tenantId) =>
        _counts.AddOrUpdate(tenantId, 0, (_, c) => Math.Max(0, c - 1));

    public IReadOnlyCollection<Guid> ActiveTenants =>
        _counts.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();
}
