using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Mod.Platform.Common.Certificates;
using Mod.Platform.Common.EventHubs;

namespace Mod.IngestApi.EventHub;

public sealed class BatchPublisher(EventHubProducerClient producer)
{
    public async Task SendAsync(
        byte[] compressedBody,
        CollectorIdentity identity,
        Guid batchId,
        int eventCount,
        DateTimeOffset receivedAt,
        CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var eventData = new EventData(compressedBody);
        eventData.Properties[EventHubMessageProperties.CollectorId] = identity.CollectorId.ToString("D");
        eventData.Properties[EventHubMessageProperties.TenantId] = identity.TenantId.ToString("D");
        eventData.Properties[EventHubMessageProperties.SiteId] = identity.SiteId.ToString("D");
        eventData.Properties[EventHubMessageProperties.BatchId] = batchId.ToString("D");
        eventData.Properties[EventHubMessageProperties.ReceivedAt] = receivedAt.ToString("O");
        eventData.Properties[EventHubMessageProperties.SchemaVersion] = "1.0";
        eventData.Properties[EventHubMessageProperties.EventCount] = eventCount.ToString();
        eventData.Properties[EventHubMessageProperties.ContentEncoding] = "gzip";
        eventData.Properties[EventHubMessageProperties.CertThumbprint] = identity.Thumbprint;

        var sendOptions = new SendEventOptions { PartitionKey = identity.CollectorId.ToString("D") };
        await producer.SendAsync([eventData], sendOptions, cts.Token);
    }
}
