// DEMO SCAFFOLDING
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Mod.PortalApi.Features.Admin.Services;
using Mod.PortalApi.Features.Simulation.Channel;
using Mod.PortalApi.Features.Simulation.Data;
using Mod.PortalApi.Features.Simulation.Endpoints;
using Mod.PortalApi.Features.Simulation.Hubs;
using Mod.PortalApi.Features.Simulation.Provisioning;
using Mod.PortalApi.Features.Simulation.Scenarios;
using Mod.PortalApi.Features.Simulation.Telemetry;
using Mod.PortalApi.Infrastructure;

namespace Mod.PortalApi.Features.Simulation;

internal static class SimulationExtensions
{
    internal static IServiceCollection AddSimulation(this IServiceCollection services)
    {
        // Singletons shared by all simulation components.
        services.AddSingleton<SimStateCache>();
        services.AddSingleton<SimRepository>();

        // Service Bus — only registered if SIM_SB_FQDN is configured.
        var simSbFqdn = Environment.GetEnvironmentVariable("SIM_SB_FQDN");
        var simSbKeyName = Environment.GetEnvironmentVariable("SIM_SB_SAS_KEY_NAME") ?? "sim-issuer";
        var simSbKey = Environment.GetEnvironmentVariable("SIM_SB_SAS_KEY") ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(simSbFqdn))
        {
            var credential = CredentialFactory.CreateDefault();
            var sbClient = new SimServiceBusClient(simSbFqdn, simSbKeyName, simSbKey, credential);
            services.AddSingleton(sbClient);

            services.AddHostedService<SimStatusReceiver>(sp =>
                new SimStatusReceiver(
                    sp.GetRequiredService<SimServiceBusClient>(),
                    sp.GetRequiredService<SimStateCache>(),
                    sp.GetRequiredService<SimRepository>(),
                    sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<SimHub>>(),
                    sp.GetRequiredService<ILogger<SimStatusReceiver>>()));
        }

        // ARM provisioner — only registered if controller subscription is configured.
        var controllerSub = Environment.GetEnvironmentVariable("CONTROLLER_SUBSCRIPTION_ID");
        if (!string.IsNullOrWhiteSpace(controllerSub))
        {
            services.AddSingleton<ContainerAppProvisioner>();

            services.AddHostedService<ProvisioningReconciler>(sp =>
                new ProvisioningReconciler(
                    sp.GetRequiredService<ContainerAppProvisioner>(),
                    sp.GetRequiredService<SimRepository>(),
                    sp.GetRequiredService<SimStateCache>(),
                    sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<SimHub>>(),
                    sp.GetRequiredService<ILogger<ProvisioningReconciler>>()));
        }
        else
        {
            // Even without ARM, run the reconciler to init cache from SQL at startup.
            services.AddHostedService<ProvisioningReconciler>(sp =>
                new ProvisioningReconciler(
                    sp.GetService<ContainerAppProvisioner>()!,
                    sp.GetRequiredService<SimRepository>(),
                    sp.GetRequiredService<SimStateCache>(),
                    sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<SimHub>>(),
                    sp.GetRequiredService<ILogger<ProvisioningReconciler>>()));
        }

        // ScenarioRunner is a singleton background service.
        services.AddSingleton<ScenarioRunner>(sp =>
            new ScenarioRunner(
                sp.GetRequiredService<SimRepository>(),
                sp.GetRequiredService<SimStateCache>(),
                sp.GetService<SimServiceBusClient>(),
                sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<SimHub>>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<ScenarioRunner>>()));
        services.AddHostedService(sp => sp.GetRequiredService<ScenarioRunner>());

        // MetricsAggregator is a singleton background service.
        services.AddSingleton<MetricsAggregator>(sp =>
            new MetricsAggregator(
                sp.GetRequiredService<SimStateCache>(),
                sp.GetRequiredService<SimRepository>(),
                sp.GetService<ContainerAppProvisioner>(),
                sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<SimHub>>(),
                sp.GetRequiredService<ILogger<MetricsAggregator>>()));
        services.AddHostedService(sp => sp.GetRequiredService<MetricsAggregator>());

        // PowerOperations is scoped (depends on scoped admin services).
        services.AddScoped<PowerOperations>(sp =>
            new PowerOperations(
                sp.GetRequiredService<ICollectorService>(),
                sp.GetRequiredService<IEnrolmentTokenService>(),
                sp.GetRequiredService<ICertificateService>(),
                sp.GetRequiredService<IAuditWriter>(),
                sp.GetService<SimServiceBusClient>(),
                sp.GetService<ContainerAppProvisioner>(),
                sp.GetRequiredService<SimRepository>(),
                sp.GetRequiredService<SimStateCache>(),
                sp.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<SimHub>>()));

        return services;
    }

    internal static IEndpointRouteBuilder MapSimulation(this IEndpointRouteBuilder app)
    {
        app.MapHub<SimHub>("/hubs/sim");
        app.MapSim();
        return app;
    }
}
