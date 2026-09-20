namespace Mod.PortalApi.Features.Admin.Models;

internal sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total);
