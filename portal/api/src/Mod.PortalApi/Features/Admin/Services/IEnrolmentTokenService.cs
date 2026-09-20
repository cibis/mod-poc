using Mod.PortalApi.Features.Admin.Models;

namespace Mod.PortalApi.Features.Admin.Services;

internal interface IEnrolmentTokenService
{
    Task<EnrolmentTokenResult> IssueTokenAsync(Guid collectorId, Actor actor);
}

internal sealed record EnrolmentTokenResult(string Token, DateTimeOffset ExpiresAt);
