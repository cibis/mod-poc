namespace Mod.PortalApi.Features.Admin.Models;

internal sealed record ConfigDocument(
    BatchingSettings Batching,
    ForwarderSettings Forwarder,
    BufferSettings Buffer,
    IntervalsSettings Intervals)
{
    internal static ConfigDocument Default() => new(
        new BatchingSettings(500, 30),
        new ForwarderSettings(4, 5),
        new BufferSettings(100000, "DropOldest"),
        new IntervalsSettings(60, 300));
}

internal sealed record BatchingSettings(int MaxSize, int MaxAgeSeconds);
internal sealed record ForwarderSettings(int MaxConcurrentBatches, int RetryDelaySeconds);
internal sealed record BufferSettings(int CapacityEvents, string OverflowStrategy);
internal sealed record IntervalsSettings(int HealthReportSeconds, int ConfigPollSeconds);
