// DEMO SCAFFOLDING
using System.Text.Json;
using Azure;
using Azure.Messaging.ServiceBus;
using Mod.Collector.Buffer;
using Mod.Collector.Config;
using Mod.Collector.Network;
using Mod.Collector.Sources;

namespace Mod.Collector.Simulation;

internal sealed class SimChannelListener : BackgroundService
{
    private readonly CollectorOptions _options;
    private readonly LinkGate _linkGate;
    private readonly ConfigService _config;
    private readonly SqliteBuffer _buffer;
    private readonly InvalidDataInjector _injector;
    private readonly SimState _simState;
    private readonly SimStatusPublisher _statusPublisher;
    private readonly ILogger<SimChannelListener> _logger;

    public SimChannelListener(
        CollectorOptions options,
        LinkGate linkGate,
        ConfigService config,
        SqliteBuffer buffer,
        InvalidDataInjector injector,
        SimState simState,
        SimStatusPublisher statusPublisher,
        ILogger<SimChannelListener> logger)
    {
        _options = options;
        _linkGate = linkGate;
        _config = config;
        _buffer = buffer;
        _injector = injector;
        _simState = simState;
        _statusPublisher = statusPublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var client = new ServiceBusClient(
            _options.SimSbFqdn,
            new AzureNamedKeyCredential(_options.SimSbKeyName, _options.SimSbKey));
        await using var receiver = client.CreateReceiver(_options.SimCommandQueue,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete });

        _logger.LogInformation("Sim channel listener connected to {Queue}", _options.SimCommandQueue);

        while (!stoppingToken.IsCancellationRequested)
        {
            ServiceBusReceivedMessage? msg;
            try
            {
                msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (ServiceBusException sbEx) when (sbEx.IsTransient)
            {
                await Task.Delay(2_000, stoppingToken);
                continue;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Sim channel receive error — reconnecting in 5 s");
                await Task.Delay(5_000, stoppingToken);
                return; // outer loop restarts the service
            }

            if (msg is null) continue;

            try
            {
                await ApplyCommandAsync(msg, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply sim command");
            }
        }
    }

    private async Task ApplyCommandAsync(ServiceBusReceivedMessage msg, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(msg.Body);
        var root = doc.RootElement;
        var commandId = root.TryGetProperty("commandId", out var cid) ? cid.GetString() : null;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        var args = root.TryGetProperty("args", out var a) ? a : default;

        switch (type)
        {
            case "SetRate":
            {
                var rate = args.TryGetProperty("eventsPerSecondPerSource", out var r) ? r.GetDouble() : 1.0;
                var intRate = (int)Math.Clamp(rate, 0.01, 50);
                intRate = Math.Max(1, intRate);
                _simState.EventsPerSecondPerSource = intRate;
                _config.SetRateAll(intRate);
                _logger.LogInformation("SetRate {Rate} eps/source", rate);
                break;
            }

            case "SetLink":
            {
                var state = args.TryGetProperty("state", out var s) ? s.GetString() : "up";
                var up = !"down".Equals(state, StringComparison.OrdinalIgnoreCase);
                _linkGate.SetLink(up);
                _logger.LogInformation("SetLink {State}", up ? "up" : "down");
                break;
            }

            case "SetPaused":
            {
                var paused = args.TryGetProperty("paused", out var p) && p.GetBoolean();
                _simState.Paused = paused;
                _config.SetPausedAll(paused);
                _logger.LogInformation("SetPaused {Paused}", paused);
                break;
            }

            case "SetBufferCapacity":
            {
                var capacity = args.TryGetProperty("capacityEvents", out var c)
                    ? Math.Clamp(c.GetInt32(), 100, 1_000_000) : 200_000;
                _buffer.SetCapacity(capacity);
                _logger.LogInformation("SetBufferCapacity {Capacity}", capacity);
                break;
            }

            case "SetInvalidShare":
            {
                var share = args.TryGetProperty("share", out var s) ? s.GetDouble() : 0.0;
                _simState.InvalidShare = share;
                _injector.SetShare(share);
                _logger.LogInformation("SetInvalidShare {Share:P0}", share);
                break;
            }

            default:
                _logger.LogWarning("Unknown sim command type: {Type}", type);
                break;
        }

        if (commandId is not null)
            _simState.LastAppliedCommandId = commandId;

        _statusPublisher.TriggerPublish();
        await Task.CompletedTask;
    }
}
