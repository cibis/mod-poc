using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Mod.ReportingApi.Auth;

namespace Mod.ReportingApi.Live;

[Authorize(Policy = "CustomerOnly")]
public sealed class LiveHub(ITenantContext tenant, ConnectedTenantTracker tracker) : Hub
{
    public override async Task OnConnectedAsync()
    {
        tracker.Add(tenant.TenantId);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tenant.TenantId}");
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        tracker.Remove(tenant.TenantId);
        await base.OnDisconnectedAsync(exception);
    }
}
