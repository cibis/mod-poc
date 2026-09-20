namespace Mod.PortalApi.Infrastructure.ServiceBus;

internal interface IQueueManager
{
    Task EnsureQueuesAsync(Guid tenantId, Guid collectorId, CancellationToken ct = default);
    Task DeleteQueuesAsync(Guid tenantId, Guid collectorId, CancellationToken ct = default);
}
