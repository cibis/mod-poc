namespace Mod.Collector.Health;

// Thread-safe in-memory counters. Per-minute fields flush to SQLite; totals never reset.
internal sealed class MinuteCounter
{
    private long _produced;
    private long _droppedAtEdge;
    private long _producedTotal;
    private long _invalidAtEdgeTotal;

    public void RecordProduced(long n = 1)
    {
        Interlocked.Add(ref _produced, n);
        Interlocked.Add(ref _producedTotal, n);
    }

    // Call for buffer-overflow drops (not edge-validation failures).
    public void RecordDroppedAtEdge(long n = 1) => Interlocked.Add(ref _droppedAtEdge, n);

    // Call for edge-validation failures — counts in both droppedAtEdge and the sticky total.
    public void RecordInvalidAtEdge(long n = 1)
    {
        Interlocked.Add(ref _droppedAtEdge, n);
        Interlocked.Add(ref _invalidAtEdgeTotal, n);
    }

    public long Produced => Interlocked.Read(ref _produced);
    public long DroppedAtEdge => Interlocked.Read(ref _droppedAtEdge);
    public long ProducedTotal => Interlocked.Read(ref _producedTotal);
    public long InvalidAtEdgeTotal => Interlocked.Read(ref _invalidAtEdgeTotal);

    public (long produced, long droppedAtEdge) Flush()
    {
        var p = Interlocked.Exchange(ref _produced, 0);
        var d = Interlocked.Exchange(ref _droppedAtEdge, 0);
        return (p, d);
    }
}
