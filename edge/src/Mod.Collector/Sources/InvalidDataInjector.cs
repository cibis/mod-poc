// DEMO SCAFFOLDING
using Mod.Collector.Models;

namespace Mod.Collector.Sources;

// Replaces a configured share of generated events with invalid ones that pass edge
// schema validation but will be dead-lettered in the cloud per the contract:
//   UnmappedSource, TimestampOutOfRange, CounterDecreaseWithoutReset, ValueOutOfRange.
internal sealed class InvalidDataInjector
{
    private double _invalidShare;   // [0,1]; set by SimChannelListener.SetInvalidShare
    private readonly Random _rng = new();

    public void SetShare(double share) => _invalidShare = Math.Clamp(share, 0.0, 1.0);

    // Returns null (keep original) or a replacement event.
    // The replacement always passes EdgeValidator; the cloud will dead-letter it.
    public CanonicalEvent? MaybeInject(CanonicalEvent original)
    {
        var share = _invalidShare;
        if (share <= 0.0 || _rng.NextDouble() >= share)
            return null;

        return (_rng.Next(4)) switch
        {
            // UnmappedSource: no AssetSourceMapping for this sourceId in the cloud
            0 => original with { SourceId = "unmapped-sim" },

            // TimestampOutOfRange: eventTime > receivedAt + 5 min
            1 => original with { EventTime = original.EventTime.AddDays(1) },

            // CounterDecreaseWithoutReset: value lower than any legitimate previous value
            2 => original with
            {
                EventType = "Counter",
                Payload = new CounterPayload("parts", 0, false),
            },

            // ValueOutOfRange: |value| > 1e9 but still finite (passes edge IsFinite check)
            _ => original with
            {
                EventType = "ProcessValue",
                Payload = new ProcessValuePayload("sim-pv", 1e12, "units"),
            },
        };
    }
}
