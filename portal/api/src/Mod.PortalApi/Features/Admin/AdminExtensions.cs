using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Mod.PortalApi.Infrastructure;
using Mod.PortalApi.Features.Admin.Audit;
using Mod.PortalApi.Features.Admin.Collectors;
using Mod.PortalApi.Features.Admin.DeadLetters;
using Mod.PortalApi.Features.Admin.Restatements;
using Mod.PortalApi.Features.Admin.Services;
using Mod.PortalApi.Features.Admin.Sites;
using Mod.PortalApi.Features.Admin.Tenants;
using Mod.PortalApi.Infrastructure.ServiceBus;
using Mod.PortalApi.Infrastructure.Sql;

namespace Mod.PortalApi.Features.Admin;

internal static class AdminExtensions
{
    internal static IServiceCollection AddAdmin(this IServiceCollection services)
    {
        var cmdSbFqdn = Environment.GetEnvironmentVariable("CMD_SB_FQDN");

        ServiceBusAdministrationClient? adminClient = null;
        ServiceBusClient? sbClient = null;

        if (!string.IsNullOrWhiteSpace(cmdSbFqdn))
        {
            var credential = CredentialFactory.CreateDefault();
            adminClient = new ServiceBusAdministrationClient(cmdSbFqdn, credential);
            sbClient = new ServiceBusClient(cmdSbFqdn, credential);
            services.AddSingleton(adminClient);
            services.AddSingleton(sbClient);
            services.AddSingleton<IQueueManager>(sp =>
                new QueueManager(sp.GetRequiredService<ServiceBusAdministrationClient>()));
        }
        else
        {
            services.AddSingleton<IQueueManager>(new NullQueueManager());
        }

        services.AddHostedService<QueueReconciler>(sp =>
            new QueueReconciler(
                sp.GetRequiredService<SqlConfig>(),
                sp.GetService<IQueueManager>(),
                sp.GetRequiredService<ILogger<QueueReconciler>>()));

        services.AddScoped<ITenantService, TenantService>();
        services.AddScoped<ISiteService, SiteService>();
        services.AddScoped<IEnrolmentTokenService, EnrolmentTokenService>();
        services.AddScoped<ICertificateService, CertificateService>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        // CollectorService needs IQueueManager and IAuditWriter
        services.AddScoped<ICollectorService, CollectorService>();

        // CommandDispatcher is singleton (manages receivers)
        services.AddSingleton<ICommandDispatcher>(sp =>
            new CommandDispatcher(
                sp.GetService<ServiceBusClient>(),
                sp.GetRequiredService<ICollectorService>(),
                sp.GetRequiredService<ILogger<CommandDispatcher>>()));

        return services;
    }

    internal static IEndpointRouteBuilder MapAdmin(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("AdminOnly");

        admin.MapTenants();
        admin.MapSites();
        admin.MapCollectors();
        admin.MapDeadLetters();
        admin.MapRestatements();
        admin.MapAudit();

        return app;
    }

    private sealed class NullQueueManager : IQueueManager
    {
        public Task EnsureQueuesAsync(Guid tenantId, Guid collectorId, CancellationToken ct = default)
            => Task.CompletedTask;
        public Task DeleteQueuesAsync(Guid tenantId, Guid collectorId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
