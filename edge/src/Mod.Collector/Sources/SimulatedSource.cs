using Mod.Collector.Buffer;
using Mod.Collector.Edge;
using Mod.Collector.Health;
using Mod.Collector.Models;

namespace Mod.Collector.Sources;

// One instance per configured source. Lifecycle is managed by ConfigService; it is not
// registered directly with DI as an IHostedService.
internal sealed class SimulatedSource : BackgroundService
{
    private readonly SourceConfig _config;
    private readonly SqliteBuffer _buffer;
    private readonly InvalidDataInjector _injector;
    private readonly EdgeValidator _validator;
    private readonly MinuteCounter _minute;
    private readonly ILogger<SimulatedSource> _logger;

    private volatile int _eventsPerSecond;
    private volatile bool _paused;
    private long _lastSequence;
    private DateTimeOffset _lastReadAt = DateTimeOffset.MinValue;

    public string SourceId => _config.SourceId;
    public long LastSequence => Interlocked.Read(ref _lastSequence);
    public DateTimeOffset LastReadAt => _lastReadAt;

    public SimulatedSource(
        SourceConfig config,
        SqliteBuffer buffer,
        InvalidDataInjector injector,
        EdgeValidator validator,
        MinuteCounter minute,
        ILogger<SimulatedSource> logger,
        int initialEventsPerSecond = 1)
    {
        _config = config;
        _buffer = buffer;
        _injector = injector;
        _validator = validator;
        _minute = minute;
        _logger = logger;
        _eventsPerSecond = Math.Max(1, initialEventsPerSecond);
    }

    public void SetRate(int eventsPerSecond) =>
        _eventsPerSecond = Math.Max(1, eventsPerSecond);

    public void SetPaused(bool paused) => _paused = paused;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rng = new Random();
        var state = "Running";
        long counterValue = 0;
        var lastShiftHour = CurrentShiftHour();

        var pvLastValue = new Dictionary<string, double>();
        var pvLastEmit = new Dictionary<string, DateTimeOffset>();

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_paused)
            {
                await Task.Delay(200, stoppingToken);
                continue;
            }

            var rate = _eventsPerSecond;
            var baseMs = 1000.0 / rate;
            var jitterMs = baseMs * 0.2 * (rng.NextDouble() * 2 - 1);
            var intervalMs = (int)Math.Max(10, baseMs + jitterMs);

            var newState = state switch
            {
                "Running" when rng.NextDouble() < 0.001 => "Idle",
                "Running" when rng.NextDouble() < 0.0002 => "Fault",
                "Running" when rng.NextDouble() < 0.0001 => "Setup",
                "Idle" when rng.NextDouble() < 0.10 => "Running",
                "Fault" when rng.NextDouble() < 0.05 => "Running",
                "Setup" when rng.NextDouble() < 0.03 => "Running",
                _ => state,
            };

            if (newState != state)
            {
                var prevState = state;
                state = newState;
                if (newState == "Fault")
                    await EmitAsync(_config.SourceId, "Fault", new FaultPayload("SIM-FAULT", true), stoppingToken);
                else if (prevState == "Fault")
                    await EmitAsync(_config.SourceId, "Fault", new FaultPayload("SIM-FAULT", false), stoppingToken);
                await EmitAsync(_config.SourceId, "StateChange", new StateChangePayload(newState), stoppingToken);
            }

            if (state == "Running")
            {
                var now = DateTimeOffset.UtcNow;

                var shiftHour = CurrentShiftHour();
                if (shiftHour != lastShiftHour)
                {
                    lastShiftHour = shiftHour;
                    counterValue = 0;
                    await EmitAsync(_config.SourceId, "Counter",
                        new CounterPayload(_config.CounterName, 0, true), stoppingToken);
                }
                else
                {
                    counterValue++;
                    await EmitAsync(_config.SourceId, "Counter",
                        new CounterPayload(_config.CounterName, counterValue, false), stoppingToken);
                }

                foreach (var pv in _config.ProcessValues)
                {
                    var range = pv.Max - pv.Min;
                    var noise = range * 0.05 * (rng.NextDouble() * 2 - 1);
                    var midpoint = (pv.Min + pv.Max) / 2.0;
                    var value = Math.Clamp(midpoint + noise, pv.Min, pv.Max);

                    pvLastValue.TryGetValue(pv.Name, out var lastVal);
                    pvLastEmit.TryGetValue(pv.Name, out var lastEmit);

                    var deadbandRef = Math.Abs(lastVal) < 1e-9 ? 1.0 : Math.Abs(lastVal);
                    var change = Math.Abs(value - lastVal) / deadbandRef;
                    var elapsed = (now - lastEmit).TotalSeconds;

                    if (change > 0.01 || elapsed >= 10.0)
                    {
                        pvLastValue[pv.Name] = value;
                        pvLastEmit[pv.Name] = now;
                        await EmitAsync(_config.SourceId, "ProcessValue",
                            new ProcessValuePayload(pv.Name, value, pv.Unit), stoppingToken);
                    }
                }
            }

            await Task.Delay(intervalMs, stoppingToken);
        }
    }

    private async Task EmitAsync(string sourceId, string eventType, object payload, CancellationToken ct)
    {
        var ev = new CanonicalEvent
        {
            EventId = Guid.NewGuid().ToString(),
            SourceId = sourceId,
            Sequence = 0,
            EventTime = DateTimeOffset.UtcNow,
            ObservedTime = DateTimeOffset.UtcNow,
            EventType = eventType,
            Payload = payload,
        };

        var final = _injector.MaybeInject(ev) ?? ev;

        if (!_validator.IsValid(final, out var reason))
        {
            _logger.LogWarning("Event dropped at edge: {Reason}", reason);
            _minute.RecordInvalidAtEdge();
            return;
        }

        try
        {
            await _buffer.AppendAsync(
                final.SourceId,
                final.EventType,
                final.EventTime,
                seq =>
                {
                    Interlocked.Exchange(ref _lastSequence, seq);
                    _lastReadAt = DateTimeOffset.UtcNow;
                    return CanonicalMapper.ToJson(final with { Sequence = seq });
                },
                ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append event to buffer");
        }
    }

    private static int CurrentShiftHour() => (DateTimeOffset.UtcNow.Hour / 8) * 8;
}
