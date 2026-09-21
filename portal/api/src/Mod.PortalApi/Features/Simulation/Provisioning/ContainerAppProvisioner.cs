// DEMO SCAFFOLDING
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Azure.ResourceManager.Models;
using Azure.ResourceManager.Resources;
using Mod.PortalApi.Infrastructure;

namespace Mod.PortalApi.Features.Simulation.Provisioning;

// Creates and deletes collector container apps in the controller resource group,
// and reads running replica counts of platform apps from the production resource group.
internal sealed class ContainerAppProvisioner
{
    private readonly ArmClient _arm;
    private readonly string _controllerSubscriptionId;
    private readonly string _controllerResourceGroup;
    private readonly string _environmentId;
    private readonly string _location;
    private readonly string _collectorImage;
    private readonly string _registryServer;
    private readonly string _pullIdentityId;
    private readonly string _simSbFqdn;
    private readonly string _managementUrl;
    private readonly string _collectorSoftwareVersion;
    private readonly string _productionSubscriptionId;
    private readonly string _productionResourceGroup;
    private readonly string _collectorClientCertB64;

    internal ContainerAppProvisioner()
    {
        _controllerSubscriptionId = Env("CONTROLLER_SUBSCRIPTION_ID");
        _controllerResourceGroup = Env("CONTROLLER_RESOURCE_GROUP");
        _environmentId = Env("CONTROLLER_ENVIRONMENT_ID");
        _location = Env("CONTROLLER_LOCATION");
        _collectorImage = Env("COLLECTOR_IMAGE");
        _registryServer = _collectorImage.Split('/')[0];
        _pullIdentityId = Env("COLLECTOR_PULL_IDENTITY_ID");
        _simSbFqdn = Env("SIM_SB_FQDN");
        _managementUrl = Env("MANAGEMENT_URL");
        _collectorSoftwareVersion = _collectorImage.Contains(':')
            ? _collectorImage.Split(':')[^1]
            : "latest";
        _productionSubscriptionId = Env("PRODUCTION_SUBSCRIPTION_ID");
        _productionResourceGroup = Env("PRODUCTION_RESOURCE_GROUP");
        // PoC: shared client cert loaded at portal startup; passed to each collector at power-on.
        // Production: each collector generates its own cert via /v1/enrol; no cert env var needed here.
        _collectorClientCertB64 = Env("COLLECTOR_CLIENT_CERT_B64");

        var credential = CredentialFactory.CreateDefault();
        _arm = new ArmClient(credential);
    }

    // Returns the container app name assigned for a given collector.
    internal static string AppName(Guid collectorId)
        => $"col-{collectorId:N}"[..15]; // "col-" + first 11 hex chars of no-dash GUID

    // Starts the container app creation. Returns an ARM long-running operation that the caller
    // polls. State transitions (Provisioning → Running / Failed) are handled by the caller.
    internal async Task<ArmOperation<ContainerAppResource>> StartCreateAsync(
        Guid collectorId, Guid tenantId, Guid siteId, string simKeyName, string simKey,
        CancellationToken ct = default)
    {
        var rgId = ResourceGroupResource.CreateResourceIdentifier(
            _controllerSubscriptionId, _controllerResourceGroup);
        var rg = _arm.GetResourceGroupResource(rgId);
        var apps = rg.GetContainerApps();

        var appName = AppName(collectorId);
        var appData = BuildAppData(collectorId, tenantId, siteId, simKeyName, simKey);

        return await apps.CreateOrUpdateAsync(WaitUntil.Started, appName, appData, ct);
    }

    internal async Task DeleteAsync(string appName, CancellationToken ct = default)
    {
        try
        {
            var appId = ContainerAppResource.CreateResourceIdentifier(
                _controllerSubscriptionId, _controllerResourceGroup, appName);
            var app = _arm.GetContainerAppResource(appId);
            await app.DeleteAsync(WaitUntil.Completed, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 404) { /* already gone */ }
    }

    // Lists all container apps in the controller RG tagged with mod-sim-collector.
    internal async Task<IReadOnlyList<(string AppName, Guid CollectorId)>> ListSimCollectorAppsAsync(
        CancellationToken ct = default)
    {
        var rgId = ResourceGroupResource.CreateResourceIdentifier(
            _controllerSubscriptionId, _controllerResourceGroup);
        var rg = _arm.GetResourceGroupResource(rgId);
        var result = new List<(string, Guid)>();
        await foreach (var app in rg.GetContainerApps().GetAllAsync(cancellationToken: ct))
        {
            if (app.Data.Tags.TryGetValue("mod-sim-collector", out var idStr) &&
                Guid.TryParse(idStr, out var collectorId))
            {
                result.Add((app.Data.Name, collectorId));
            }
        }
        return result;
    }

    // Returns the active-revision replica count for a platform app in the production resource group.
    internal async Task<int> GetRunningReplicaCountAsync(string appName, CancellationToken ct = default)
    {
        try
        {
            var appId = ContainerAppResource.CreateResourceIdentifier(
                _productionSubscriptionId, _productionResourceGroup, appName);
            var app = _arm.GetContainerAppResource(appId);
            await foreach (var revision in app.GetContainerAppRevisions().GetAllAsync(cancellationToken: ct))
            {
                if (revision.Data.IsActive == true)
                    return revision.Data.Replicas ?? 0;
            }
            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private ContainerAppData BuildAppData(
        Guid collectorId, Guid tenantId, Guid siteId, string simKeyName, string simKey)
    {
        var config = new ContainerAppConfiguration
        {
            ActiveRevisionsMode = ContainerAppActiveRevisionsMode.Single,
        };
        config.Registries.Add(new ContainerAppRegistryCredentials
        {
            Server = _registryServer,
            Identity = _pullIdentityId,
        });
        // PoC: shared client cert passed as a secret; one cert for all collectors.
        // Production: cert is issued per-collector via /v1/enrol; no cert secret needed here.
        config.Secrets.Add(new ContainerAppWritableSecret { Name = "collector-client-cert", Value = _collectorClientCertB64 });
        config.Secrets.Add(new ContainerAppWritableSecret { Name = "sim-sb-key", Value = simKey });

        var container = new ContainerAppContainer
        {
            Name = "collector",
            Image = _collectorImage,
            Resources = new AppContainerResources { Cpu = 0.25, Memory = "0.5Gi" },
        };
        container.VolumeMounts.Add(new ContainerAppVolumeMount { VolumeName = "data", MountPath = "/data" });
        container.Probes.Add(new ContainerAppProbe
        {
            ProbeType = ContainerAppProbeType.Liveness,
            HttpGet = new ContainerAppHttpRequestInfo(8080) { Path = "/healthz" },
        });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "COLLECTOR_ID", Value = collectorId.ToString("D") });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "TENANT_ID", Value = tenantId.ToString("D") });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "SITE_ID", Value = siteId.ToString("D") });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "COLLECTOR_CLIENT_CERT_B64", SecretRef = "collector-client-cert" });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "MANAGEMENT_URL", Value = _managementUrl });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "DATA_DIR", Value = "/data" });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "SIM_SB_FQDN", Value = _simSbFqdn });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "SIM_COMMAND_QUEUE", Value = SimChannel.CollectorQueueName(collectorId) });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "SIM_SB_KEY_NAME", Value = simKeyName });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "SIM_SB_KEY", SecretRef = "sim-sb-key" });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "SIM_STATUS_QUEUE", Value = "sim-status" });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "SIM_INITIAL_RATE", Value = "1" });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "COLLECTOR_SOFTWARE_VERSION", Value = _collectorSoftwareVersion });
        container.Env.Add(new ContainerAppEnvironmentVariable { Name = "LOG_LEVEL", Value = "Information" });

        var template = new ContainerAppTemplate
        {
            Scale = new ContainerAppScale { MinReplicas = 1, MaxReplicas = 1 },
        };
        template.Containers.Add(container);
        template.Volumes.Add(new ContainerAppVolume { Name = "data", StorageType = ContainerAppStorageType.EmptyDir });

        var appData = new ContainerAppData(new AzureLocation(_location))
        {
            ManagedEnvironmentId = new ResourceIdentifier(_environmentId),
            Configuration = config,
            Template = template,
            Identity = new ManagedServiceIdentity(ManagedServiceIdentityType.UserAssigned),
        };
        appData.Identity.UserAssignedIdentities[new ResourceIdentifier(_pullIdentityId)] = new UserAssignedIdentity();
        appData.Tags["mod-sim-collector"] = collectorId.ToString("D");
        appData.Tags["mod-demo"] = "true";

        return appData;
    }

    private static string Env(string name) =>
        Environment.GetEnvironmentVariable(name) ?? string.Empty;
}

// Forward reference used inside BuildAppData to avoid circular reference with Channel namespace.
file static class SimChannel
{
    internal static string CollectorQueueName(Guid id) => $"sim~{id:D}";
}
