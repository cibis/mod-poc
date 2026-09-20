using Azure.Messaging.ServiceBus.Administration;

namespace Mod.PortalApi.Infrastructure.ServiceBus;

internal sealed class QueueManager(ServiceBusAdministrationClient adminClient) : IQueueManager
{
    public async Task EnsureQueuesAsync(Guid tenantId, Guid collectorId, CancellationToken ct = default)
    {
        await EnsureQueueAsync(RequestQueueName(tenantId, collectorId), ct);
        await EnsureQueueAsync(ReplyQueueName(tenantId, collectorId), ct);
    }

    public async Task DeleteQueuesAsync(Guid tenantId, Guid collectorId, CancellationToken ct = default)
    {
        await DeleteIfExistsAsync(RequestQueueName(tenantId, collectorId), ct);
        await DeleteIfExistsAsync(ReplyQueueName(tenantId, collectorId), ct);
    }

    private async Task EnsureQueueAsync(string name, CancellationToken ct)
    {
        if (await adminClient.QueueExistsAsync(name, ct))
            return;

        var options = new CreateQueueOptions(name)
        {
            DefaultMessageTimeToLive = TimeSpan.FromMinutes(5),
            LockDuration = TimeSpan.FromSeconds(30),
            MaxDeliveryCount = 3,
            DeadLetteringOnMessageExpiration = false,
        };
        await adminClient.CreateQueueAsync(options, ct);
    }

    private async Task DeleteIfExistsAsync(string name, CancellationToken ct)
    {
        if (await adminClient.QueueExistsAsync(name, ct))
            await adminClient.DeleteQueueAsync(name, ct);
    }

    internal static string RequestQueueName(Guid tenantId, Guid collectorId) =>
        $"cmd/{tenantId:D}/{collectorId:D}";

    internal static string ReplyQueueName(Guid tenantId, Guid collectorId) =>
        $"reply/{tenantId:D}/{collectorId:D}";
}
