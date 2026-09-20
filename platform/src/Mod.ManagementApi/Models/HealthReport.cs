namespace Mod.ManagementApi.Models;

public sealed class HealthReport
{
    public Guid CollectorId { get; init; }
    public DateTimeOffset ReportedAt { get; init; }
    public string SoftwareVersion { get; init; } = "";
    public int ConfigVersion { get; init; }
    public string ConfigHash { get; init; } = "";
    public int BufferDepthEvents { get; init; }
    public int BufferCapacityEvents { get; init; }
    public DateTimeOffset? OldestBufferedEventTime { get; init; }
    public long OverflowDroppedTotal { get; init; }
    public long RejectedBatchesTotal { get; init; }
    public long InvalidAtEdgeTotal { get; init; }
    public int ClockOffsetMs { get; init; }
    public long DiskFreeBytes { get; init; }
    public IReadOnlyList<SourceHealth> Sources { get; init; } = [];
    public IReadOnlyList<MinuteCount> MinuteCounts { get; init; } = [];
}

public sealed class SourceHealth
{
    public string SourceId { get; init; } = "";
    public DateTimeOffset? LastReadAt { get; init; }
    public long LastSequence { get; init; }
}

public sealed class MinuteCount
{
    public DateTime MinuteStart { get; init; }
    public int Produced { get; init; }
    public int DroppedAtEdge { get; init; }
}
