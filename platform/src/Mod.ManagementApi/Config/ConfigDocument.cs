namespace Mod.ManagementApi.Config;

public sealed record ConfigDocument(
    int ConfigVersion,
    Guid CollectorId,
    Guid TenantId,
    Guid SiteId,
    string IngestUrl,
    string ManagementUrl,
    IReadOnlyList<SourceConfig> Sources,
    BatchingConfig Batching,
    ForwarderConfig Forwarder,
    BufferConfig Buffer,
    IntervalsConfig Intervals,
    bool CommandChannelEnabled);

public sealed record SourceConfig(
    string SourceId,
    string AssetName,
    string AssetType,
    string CounterName,
    ProcessValueConfig? ProcessValue);

public sealed record ProcessValueConfig(
    string Name,
    string Unit,
    double? Min,
    double? Max);

public sealed record BatchingConfig(int MaxEvents, int MaxBytes, int FlushIntervalMs);
public sealed record ForwarderConfig(int MaxBatchesPerSecond, int RetryBaseMs, int RetryMaxMs);
public sealed record BufferConfig(int CapacityEvents);
public sealed record IntervalsConfig(int ConfigPollSeconds, int HealthReportSeconds);
