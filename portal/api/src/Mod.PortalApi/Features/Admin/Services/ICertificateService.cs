namespace Mod.PortalApi.Features.Admin.Services;

internal interface ICertificateService
{
    // Revokes all active certificates without changing collector status (used by simulator power-off)
    Task RevokeAllActiveAsync(Guid collectorId, string reason);
}
