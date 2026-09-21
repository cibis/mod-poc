using Mod.PortalApi.Features.Admin.Models;

namespace Mod.PortalApi.Features.Admin.Services;

internal interface ICollectorService
{
    Task<PagedResult<CollectorSummary>> ListCollectorsAsync(
        Guid? tenantId, Guid? siteId, string? status, int page, int pageSize);

    Task<CollectorRow> RegisterCollectorAsync(
        Guid siteId, string name, bool commandChannelEnabled, Actor actor);

    Task<CollectorDetail?> GetCollectorDetailAsync(Guid collectorId);

    Task<CollectorConfigRow> UpdateConfigAsync(
        Guid collectorId, ConfigUpdateSettings settings, bool commandChannelEnabled, Actor actor);

    Task UpdateMappingsAsync(
        Guid collectorId, IReadOnlyList<MappingInput> mappings, Actor actor);

    Task RevokeAsync(Guid collectorId, string reason, Actor actor);

    Task<IReadOnlyList<HealthHistoryRow>> GetHealthHistoryAsync(
        Guid collectorId, DateTime? from, DateTime? to);

    Task<(Guid tenantId, int healthIntervalSeconds)?> GetCollectorTenantInfoAsync(Guid collectorId);
}

internal sealed record CollectorSummary(
    Guid CollectorId, string Name,
    Guid TenantId, string TenantName,
    Guid SiteId, string SiteName, string RegionLabel,
    string Status, DateTime? LastSeenAt, string? SoftwareVersion,
    int? ReportedConfigVersion, int? ConfigVersion,
    DateTime? CertificateExpiresAt,
    int? BufferDepthEvents, int? BufferCapacityEvents,
    int? FreshnessAgeSeconds);

internal sealed record CollectorRow(
    Guid CollectorId, Guid TenantId, Guid SiteId, string Name,
    string Status, bool CommandChannelEnabled, DateTime CreatedAt);

internal sealed record CollectorDetail(
    CollectorRow Collector,
    string TenantName,
    string SiteName,
    string RegionLabel,
    DateTime? LastSeenAt,
    string? SoftwareVersion,
    CollectorConfigRow Config,
    IReadOnlyList<MappingDetail> Mappings,
    IReadOnlyList<CertificateRow> Certificates,
    HealthRow? Health,
    IReadOnlyList<ReconciliationRow> Reconciliation);

internal sealed record CollectorConfigRow(
    Guid CollectorId, int Version, string SettingsJson, bool CommandChannelEnabled,
    DateTime UpdatedAt, string UpdatedBy);

internal sealed record MappingInput(string SourceId, Guid AssetId);

internal sealed record MappingDetail(
    string SourceId, Guid AssetId, string? AssetName,
    DateTime ValidFrom, DateTime? ValidTo);

internal sealed record CertificateRow(
    string Thumbprint, string SerialNumber,
    DateTime IssuedAt, DateTime ExpiresAt,
    DateTime? SupersededAt, DateTime? RevokedAt, string? RevocationReason);

internal sealed record HealthRow(
    DateTime ReceivedAt, int BufferDepthEvents, int BufferCapacityEvents,
    long OverflowDroppedTotal, string? SoftwareVersion, int? ConfigVersion,
    int? ClockOffsetMs);

internal sealed record ReconciliationRow(
    DateTime MinuteStart, int? ProducedCount, int? DroppedAtEdgeCount,
    int AcceptedCount, int DeadLetteredCount);

internal sealed record HealthHistoryRow(
    DateTime ReceivedAt, int BufferDepthEvents, int BufferCapacityEvents,
    long OverflowDroppedTotal);

internal sealed record ConfigUpdateSettings(
    BatchingSettingsInput? Batching,
    ForwarderSettingsInput? Forwarder,
    BufferSettingsInput? Buffer,
    IntervalsSettingsInput? Intervals);

internal sealed record BatchingSettingsInput(int? MaxSize, int? MaxAgeSeconds);
internal sealed record ForwarderSettingsInput(int? MaxConcurrentBatches, int? RetryDelaySeconds);
internal sealed record BufferSettingsInput(int? CapacityEvents, string? OverflowStrategy);
internal sealed record IntervalsSettingsInput(int? HealthReportSeconds, int? ConfigPollSeconds);
