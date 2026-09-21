using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Mod.ReportingApi.Live;

[Authorize(Policy = "CustomerOnly")]
public sealed class LiveHub(ConnectedTenantTracker tracker) : Hub
{
    // Read tenant from JWT claims directly — hub lifecycle methods run in a scope that does
    // not share the request middleware scope where TenantContext is populated.
    private Guid TenantId =>
        Guid.TryParse(Context.User?.FindFirst("tenant_id")?.Value, out var tid) ? tid : Guid.Empty;

    public override async Task OnConnectedAsync()
    {
        var tid = TenantId;
        if (tid != Guid.Empty)
        {
            tracker.Add(tid);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"tenant:{tid}");
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var tid = TenantId;
        if (tid != Guid.Empty)
            tracker.Remove(tid);
        await base.OnDisconnectedAsync(exception);
    }
}
