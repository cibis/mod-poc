namespace Mod.Platform.Common.Registry;

public record CollectorRecord(
    Guid CollectorId,
    Guid TenantId,
    Guid SiteId,
    string Status);
