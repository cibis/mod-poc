// DEMO SCAFFOLDING
using System.Buffers;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Messaging.ServiceBus;
using Mod.Collector.Buffer;
using Mod.Collector.Config;
using Mod.Collector.Forwarding;
using Mod.Collector.Health;
using Mod.Collector.Identity;
using Mod.Collector.Network;

namespace Mod.Collector.Simulation;

internal sealed class SimStatusPublisher : BackgroundService
{
    private readonly CollectorOptions _options;
    private readonly ConfigService _config;
    private readonly IdentityStore _identity;
    private readonly SqliteBuffer _buffer;
    private readonly LinkGate _linkGate;
    private readonly MinuteCounter _minute;
    private readonly Forwarder _forwarder;
    private readonly SimState _simState;
    private readonly ILogger<SimStatusPublisher> _logger;

    // Semaphore used to wake the publish loop immediately after a command is applied.
    private readonly SemaphoreSlim _trigger = new(0, 1);

    public SimStatusPublisher(
        CollectorOptions options,
        ConfigService config,
        IdentityStore identity,
        SqliteBuffer buffer,
        LinkGate linkGate,
        MinuteCounter minute,
        Forwarder forwarder,
        SimState simState,
        ILogger<SimStatusPublisher> logger)
    {
        _options = options;
        _config = config;
        _identity = identity;
        _buffer = buffer;
        _linkGate = linkGate;
        _minute = minute;
        _forwarder = forwarder;
        _simState = simState;
        _logger = logger;
    }

    public void TriggerPublish()
    {
        if (_trigger.CurrentCount == 0)
            _trigger.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ServiceBusClient? client = null;
        ServiceBusSender? sender = null;

        try
        {
            client = new ServiceBusClient(
                _options.SimSbFqdn,
                new AzureSasCredential(_options.SimStatusSas));
            sender = client.CreateSender(_options.SimStatusQueue);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Wait for an explicit trigger or a 2 s timeout.
                await _trigger.WaitAsync(2_000, stoppingToken);

                try
                {
                    await PublishAsync(sender, stoppingToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish sim status");
                }
            }
        }
        catch (OperationCanceledException) { /* host shutdown */ }
        finally
        {
            if (sender is not null) await sender.DisposeAsync();
            if (client is not null) await client.DisposeAsync();
        }
    }

    private async Task PublishAsync(ServiceBusSender sender, CancellationToken ct)
    {
        var bufferDepth = 0;
        try { bufferDepth = await _buffer.CountAsync(ct); } catch { /* best-effort */ }

        var body = BuildStatusJson(bufferDepth);
        var msg = new ServiceBusMessage(Encoding.UTF8.GetBytes(body))
        {
            ContentType = "application/json",
        };
        msg.ApplicationProperties["mod.collectorId"] = _options.CollectorId;
        await sender.SendMessageAsync(msg, ct);
    }

    private string BuildStatusJson(int bufferDepth)
    {
        var buf = new ArrayBufferWriter<byte>();
        using var w = new Utf8JsonWriter(buf);
        w.WriteStartObject();
        w.WriteString("collectorId", _options.CollectorId);
        w.WriteString("sentAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        w.WriteString("softwareVersion", _options.CollectorSoftwareVersion);
        w.WriteBoolean("enrolled", _identity.IsEnrolled);
        w.WriteBoolean("authRejected", _identity.AuthRejected);
        w.WriteString("linkState", _linkGate.IsUp ? "up" : "down");
        w.WriteBoolean("paused", _simState.Paused);
        w.WriteNumber("eventsPerSecondPerSource", _simState.EventsPerSecondPerSource);
        w.WriteNumber("invalidShare", _simState.InvalidShare);
        w.WriteNumber("bufferDepthEvents", bufferDepth);
        w.WriteNumber("bufferCapacityEvents", _config.CurrentConfig?.CapacityEvents ?? 0);
        w.WriteBoolean("bufferOverflowEver", _buffer.BufferOverflowEver);
        w.WriteNumber("overflowDroppedTotal", _buffer.OverflowDroppedTotal);
        w.WriteNumber("producedTotal", _minute.ProducedTotal);
        w.WriteNumber("ackedTotal", _forwarder.AckedTotal);
        if (_forwarder.LastAckAt.HasValue)
            w.WriteString("lastAckAt", _forwarder.LastAckAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        else
            w.WriteNull("lastAckAt");
        var lastCmd = _simState.LastAppliedCommandId;
        if (lastCmd is not null)
            w.WriteString("lastAppliedCommandId", lastCmd);
        else
            w.WriteNull("lastAppliedCommandId");
        w.WriteEndObject();
        w.Flush();
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }
}
