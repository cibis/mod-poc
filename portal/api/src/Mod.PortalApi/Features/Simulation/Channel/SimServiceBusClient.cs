// DEMO SCAFFOLDING
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Mod.PortalApi.Features.Simulation.Models;

namespace Mod.PortalApi.Features.Simulation.Channel;

// Wraps the simulator Service Bus namespace (separate from the product command namespace).
// Manages sim/{collectorId} queues, exposes key credentials for collectors, sends commands.
internal sealed class SimServiceBusClient : IAsyncDisposable
{
    private readonly string _sasKeyName;
    private readonly string _sasKey;
    private readonly ServiceBusAdministrationClient _adminClient;
    private readonly ServiceBusClient _client;

    internal SimServiceBusClient(
        string fqdn, string sasKeyName, string sasKey,
        Azure.Core.TokenCredential credential)
    {
        _sasKeyName = sasKeyName;
        _sasKey = sasKey;
        _adminClient = new ServiceBusAdministrationClient(fqdn, credential);
        _client = new ServiceBusClient(fqdn, credential);
    }

    internal async Task EnsureCollectorQueueAsync(Guid collectorId, CancellationToken ct = default)
    {
        // PoC: Azure SB normalizes '/' to '~' in stored queue names but the management API
        // requires the '/' form for creation. QueueExistsAsync with the '~' form returns false
        // even when the queue exists, so we skip the check and just ignore 409 on create.
        var creationPath = CollectorQueueCreationPath(collectorId);
        try
        {
            await _adminClient.CreateQueueAsync(
                new CreateQueueOptions(creationPath)
                {
                    DefaultMessageTimeToLive = TimeSpan.FromSeconds(60),
                    DeadLetteringOnMessageExpiration = false,
                }, ct);
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 409) { /* already exists */ }
    }

    internal async Task DeleteCollectorQueueAsync(Guid collectorId, CancellationToken ct = default)
    {
        // Use creation-path form; ignore 404 if already gone.
        var creationPath = CollectorQueueCreationPath(collectorId);
        try { await _adminClient.DeleteQueueAsync(creationPath, ct); }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404) { /* already gone */ }
    }

    internal string GetKeyName() => _sasKeyName;
    internal string GetKey() => _sasKey;

    internal async Task SendCommandAsync(Guid collectorId, SimCommandBody command, CancellationToken ct = default)
    {
        var queueName = CollectorQueueName(collectorId);
        await using var sender = _client.CreateSender(queueName);
        var body = JsonSerializer.Serialize(command, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        var message = new ServiceBusMessage(body)
        {
            MessageId = command.CommandId.ToString("D"),
            TimeToLive = TimeSpan.FromSeconds(60),
        };
        await sender.SendMessageAsync(message, ct);
    }

    internal ServiceBusClient GetClient() => _client;

    // Stored name (post-normalisation) used for SAS tokens, senders, receivers, and delete.
    internal static string CollectorQueueName(Guid collectorId) => $"sim~{collectorId:D}";

    // Creation path submitted to the SB management API; Azure normalises '/' to '~' on write.
    private static string CollectorQueueCreationPath(Guid collectorId) => $"sim/{collectorId:D}";

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
