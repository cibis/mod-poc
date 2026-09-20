namespace Mod.Platform.Common.Certificates;

public record CollectorIdentity(
    Guid CollectorId,
    Guid TenantId,
    Guid SiteId,
    string Thumbprint);
