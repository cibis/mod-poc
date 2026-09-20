// DEMO SCAFFOLDING
namespace Mod.Collector.Simulation;

// Shared mutable sim state written by SimChannelListener and read by SimStatusPublisher.
internal sealed class SimState
{
    private readonly object _lock = new();
    private bool _paused;
    private int _eventsPerSecondPerSource;
    private double _invalidShare;
    private string? _lastAppliedCommandId;

    public SimState(CollectorOptions options)
    {
        _eventsPerSecondPerSource = Math.Max(1, options.SimInitialRate);
    }

    public bool Paused
    {
        get { lock (_lock) return _paused; }
        set { lock (_lock) _paused = value; }
    }

    public int EventsPerSecondPerSource
    {
        get { lock (_lock) return _eventsPerSecondPerSource; }
        set { lock (_lock) _eventsPerSecondPerSource = Math.Max(1, value); }
    }

    public double InvalidShare
    {
        get { lock (_lock) return _invalidShare; }
        set { lock (_lock) _invalidShare = Math.Clamp(value, 0.0, 1.0); }
    }

    public string? LastAppliedCommandId
    {
        get { lock (_lock) return _lastAppliedCommandId; }
        set { lock (_lock) _lastAppliedCommandId = value; }
    }
}
