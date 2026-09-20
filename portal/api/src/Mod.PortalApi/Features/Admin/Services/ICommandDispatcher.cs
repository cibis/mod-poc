using Mod.PortalApi.Features.Admin.Models;

namespace Mod.PortalApi.Features.Admin.Services;

internal interface ICommandDispatcher
{
    Task<CommandResult> SendCommandAsync(
        Guid collectorId, Guid tenantId, string type, Actor actor);
}

internal sealed record CommandResult(
    Guid RequestId,
    string Outcome,
    object? Result,
    string? Error,
    long ElapsedMs);
