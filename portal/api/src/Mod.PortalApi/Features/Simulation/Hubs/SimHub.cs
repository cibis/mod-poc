// DEMO SCAFFOLDING
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Mod.PortalApi.Features.Simulation.Hubs;

[Authorize(Policy = "AdminOnly")]
internal sealed class SimHub : Hub
{
}
