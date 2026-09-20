using Mod.PortalApi.Features.Admin.Models;

namespace Mod.PortalApi.Features.Admin.Services;

internal interface IAuditWriter
{
    Task WriteAsync(Actor actor, string action, string targetType, string targetId,
        Guid? tenantId, string? detailsJson = null);
}
