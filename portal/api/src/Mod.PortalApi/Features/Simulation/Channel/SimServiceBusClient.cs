// DEMO SCAFFOLDING
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Mod.PortalApi.Features.Simulation.Models;

namespace Mod.PortalApi.Features.Simulation.Channel;

// Wraps the simulator Service Bus namespace (separate from the product command namespace).
// Manages sim/{collectorId} queues, mints SAS tokens for collectors, sends commands.
internal sealed class SimServiceBusClient : IAsyncDisposable
{
    private readonly string _fqdn;
    private readonly string _sasKeyName;
    private readonly string _sasKey;
    private readonly ServiceBusAdministrationClient _adminClient;
    private readonly ServiceBusClient _client;

    internal SimServiceBusClient(
        string fqdn, string sasKeyName, string sasKey,
        Azure.Core.TokenCredential credential)
    {
        _fqdn = fqdn;
        _sasKeyName = sasKeyName;
        _sasKey = sasKey;
        _adminClient = new ServiceBusAdministrationClient(fqdn, credential);
        _client = new ServiceBusClient(fqdn, credential);
    }

    internal async Task EnsureCollectorQueueAsync(Guid collectorId, CancellationToken ct = default)
    {
        var name = CollectorQueueName(collectorId);
        if (!await _adminClient.QueueExistsAsync(name, ct))
        {
            await _adminClient.CreateQueueAsync(
                new CreateQueueOptions(name)
                {
                    DefaultMessageTimeToLive = TimeSpan.FromSeconds(60),
                    DeadLetteringOnMessageExpiration = false,
                }, ct);
        }
    }

    internal async Task DeleteCollectorQueueAsync(Guid collectorId, CancellationToken ct = default)
    {
        var name = CollectorQueueName(collectorId);
        if (await _adminClient.QueueExistsAsync(name, ct))
            await _adminClient.DeleteQueueAsync(name, ct);
    }

    // Mint a Listen SAS token valid for 30 days for a specific queue.
    internal string MintListenToken(string queueName, TimeSpan duration)
        => GenerateSasToken(ResourceUri(queueName), _sasKeyName, _sasKey, duration);

    // Mint a Send SAS token valid for 30 days for a specific queue.
    internal string MintSendToken(string queueName, TimeSpan duration)
        => GenerateSasToken(ResourceUri(queueName), _sasKeyName, _sasKey, duration);

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

    internal static string CollectorQueueName(Guid collectorId) => $"sim/{collectorId:D}";

    private string ResourceUri(string queueName)
        => $"https://{_fqdn}/{queueName}";

    private static string GenerateSasToken(
        string resourceUri, string keyName, string key, TimeSpan duration)
    {
        var expiry = DateTimeOffset.UtcNow.Add(duration);
        var expirySeconds = expiry.ToUnixTimeSeconds().ToString();
        var encodedUri = Uri.EscapeDataString(resourceUri);
        var stringToSign = $"{encodedUri}\n{expirySeconds}";
        var keyBytes = Convert.FromBase64String(key);
        using var hmac = new HMACSHA256(keyBytes);
        var signature = Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
        return $"SharedAccessSignature sr={encodedUri}" +
               $"&sig={Uri.EscapeDataString(signature)}" +
               $"&se={expirySeconds}" +
               $"&skn={keyName}";
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
    }
}
