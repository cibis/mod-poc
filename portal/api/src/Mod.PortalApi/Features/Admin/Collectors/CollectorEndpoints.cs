using System.Security.Claims;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Features.Admin.Collectors;

internal static class CollectorEndpoints
{
    internal static IEndpointRouteBuilder MapCollectors(this IEndpointRouteBuilder app)
    {
        app.MapGet("/collectors", ListCollectors);
        app.MapPost("/sites/{siteId:guid}/collectors", RegisterCollector);
        app.MapGet("/collectors/{id:guid}", GetCollector);
        app.MapPut("/collectors/{id:guid}/config", UpdateConfig);
        app.MapPut("/collectors/{id:guid}/mappings", UpdateMappings);
        app.MapPost("/collectors/{id:guid}/enrolment-tokens", IssueEnrolmentToken);
        app.MapPost("/collectors/{id:guid}/revoke", RevokeCollector);
        app.MapPost("/collectors/{id:guid}/commands", SendCommand);
        app.MapGet("/collectors/{id:guid}/health-history", GetHealthHistory);
        return app;
    }

    private static async Task<IResult> ListCollectors(
        ICollectorService svc,
        Guid? tenantId, Guid? siteId, string? status,
        int page = 1, int pageSize = 50)
    {
        var result = await svc.ListCollectorsAsync(tenantId, siteId, status, page, pageSize);
        return Results.Ok(result);
    }

    private static async Task<IResult> RegisterCollector(
        Guid siteId, RegisterCollectorRequest req,
        ICollectorService svc, IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        var collector = await svc.RegisterCollectorAsync(siteId, req.Name, req.CommandChannelEnabled, actor);
        await audit.WriteAsync(actor, "CollectorRegistered", "Collector",
            collector.CollectorId.ToString(), collector.TenantId,
            $"{{\"name\":\"{collector.Name}\",\"commandChannelEnabled\":{req.CommandChannelEnabled.ToString().ToLower()}}}");
        return Results.Created($"/api/admin/collectors/{collector.CollectorId}", collector);
    }

    private static async Task<IResult> GetCollector(Guid id, ICollectorService svc)
    {
        var detail = await svc.GetCollectorDetailAsync(id);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> UpdateConfig(
        Guid id, UpdateConfigRequest req,
        ICollectorService svc, IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        var settings = new ConfigUpdateSettings(req.Batching, req.Forwarder, req.Buffer, req.Intervals);
        var config = await svc.UpdateConfigAsync(id, settings, req.CommandChannelEnabled, actor);
        await audit.WriteAsync(actor, "CollectorConfigUpdated", "Collector", id.ToString(), null,
            $"{{\"newVersion\":{config.Version}}}");
        return Results.Ok(config);
    }

    private static async Task<IResult> UpdateMappings(
        Guid id, IReadOnlyList<MappingInput> mappings,
        ICollectorService svc, IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        await svc.UpdateMappingsAsync(id, mappings, actor);
        await audit.WriteAsync(actor, "CollectorMappingsUpdated", "Collector", id.ToString(), null);
        return Results.NoContent();
    }

    private static async Task<IResult> IssueEnrolmentToken(
        Guid id, IEnrolmentTokenService svc, IAuditWriter audit, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        var result = await svc.IssueTokenAsync(id, actor);
        await audit.WriteAsync(actor, "EnrolmentTokenIssued", "Collector", id.ToString(), null);
        return Results.Created($"/api/admin/collectors/{id}/enrolment-tokens", result);
    }

    private static async Task<IResult> RevokeCollector(
        Guid id, RevokeRequest req,
        ICollectorService svc, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        await svc.RevokeAsync(id, req.Reason, actor);
        return Results.NoContent();
    }

    private static async Task<IResult> SendCommand(
        Guid id, SendCommandRequest req,
        ICollectorService collectorSvc, ICommandDispatcher dispatcher, ClaimsPrincipal user)
    {
        var actor = GetActor(user);
        var info = await collectorSvc.GetCollectorTenantInfoAsync(id);
        if (info is null)
            return Results.NotFound();

        var result = await dispatcher.SendCommandAsync(id, info.Value.tenantId, req.Type, actor);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetHealthHistory(
        Guid id, ICollectorService svc,
        DateTime? from, DateTime? to)
    {
        var rows = await svc.GetHealthHistoryAsync(id, from, to);
        return Results.Ok(rows);
    }

    private static Actor GetActor(ClaimsPrincipal user) =>
        new(user.FindFirstValue("name") ?? "Unknown", user.FindFirstValue("kind") ?? "ModAdmin");

    private sealed record RegisterCollectorRequest(string Name, bool CommandChannelEnabled);
    private sealed record UpdateConfigRequest(
        BatchingSettingsInput? Batching,
        ForwarderSettingsInput? Forwarder,
        BufferSettingsInput? Buffer,
        IntervalsSettingsInput? Intervals,
        bool CommandChannelEnabled);
    private sealed record RevokeRequest(string Reason);
    private sealed record SendCommandRequest(string Type);
}
