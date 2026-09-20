namespace Mod.Collector.Models;

internal sealed record CollectorConfig
{
    public int ConfigVersion { get; init; }
    public required string CollectorId { get; init; }
    public required string TenantId { get; init; }
    public required string SiteId { get; init; }
    public required string IngestUrl { get; init; }
    public required string ManagementUrl { get; init; }
    public required SourceConfig[] Sources { get; init; }
    // batching
    public int MaxEvents { get; init; } = 500;
    public int MaxBytes { get; init; } = 262_144;
    public int FlushIntervalMs { get; init; } = 2_000;
    // forwarder
    public double MaxBatchesPerSecond { get; init; } = 5.0;
    public int RetryBaseMs { get; init; } = 1_000;
    public int RetryMaxMs { get; init; } = 60_000;
    // buffer
    public int CapacityEvents { get; init; } = 200_000;
    // intervals
    public int ConfigPollSeconds { get; init; } = 30;
    public int HealthReportSeconds { get; init; } = 30;
    public bool CommandChannelEnabled { get; init; }
}

internal sealed record SourceConfig
{
    public required string SourceId { get; init; }
    public string AssetName { get; init; } = string.Empty;
    public string AssetType { get; init; } = string.Empty;
    public string CounterName { get; init; } = "parts";
    public ProcessValueSpec[] ProcessValues { get; init; } = [];
}

internal sealed record ProcessValueSpec(string Name, double Min, double Max, string Unit);
