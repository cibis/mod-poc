using System.Security.Claims;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Features.Admin.Tenants;

internal static class TenantEndpoints
{
    internal static IEndpointRouteBuilder MapTenants(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenants", ListTenants);
        app.MapPost("/tenants", CreateTenant);
        app.MapGet("/tenants/{tenantId:guid}/hierarchy", GetHierarchy);
        return app;
    }

    private static async Task<IResult> ListTenants(ITenantService svc)
    {
        var tenants = await svc.ListTenantsAsync();
        return Results.Ok(tenants);
    }

    private static async Task<IResult> CreateTenant(
        CreateTenantRequest req,
        ITenantService svc,
        IAuditWriter audit,
        ClaimsPrincipal user)
    {
        if (!string.Equals(req.IsolationMode, "Pooled", StringComparison.OrdinalIgnoreCase))
            return Results.Problem(type: "DedicatedNotSupportedInPoc", statusCode: 400,
                title: "Only Pooled isolation mode is supported in this PoC.");

        var actor = GetActor(user);
        TenantRow tenant;
        try { tenant = await svc.CreateTenantAsync(req.Name, req.IsolationMode, actor); }
        catch (InvalidOperationException ex) when (ex.Message == "DedicatedNotSupportedInPoc")
        {
            return Results.Problem(type: "DedicatedNotSupportedInPoc", statusCode: 400,
                title: "Only Pooled isolation mode is supported in this PoC.");
        }

        await audit.WriteAsync(actor, "TenantCreated", "Tenant", tenant.TenantId.ToString(),
            tenant.TenantId, $"{{\"name\":\"{tenant.Name}\"}}");

        return Results.Created($"/api/admin/tenants/{tenant.TenantId}", tenant);
    }

    private static async Task<IResult> GetHierarchy(Guid tenantId, ITenantService svc)
    {
        var h = await svc.GetHierarchyAsync(tenantId);
        return h is null ? Results.NotFound() : Results.Ok(h);
    }

    private static Actor GetActor(ClaimsPrincipal user) =>
        new(user.FindFirstValue("name") ?? "Unknown", user.FindFirstValue("kind") ?? "ModAdmin");

    private sealed record CreateTenantRequest(string Name, string IsolationMode);
}
