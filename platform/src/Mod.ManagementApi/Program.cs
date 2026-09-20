using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mod.ManagementApi.Config;
using Mod.ManagementApi.Endpoints;
using Mod.ManagementApi.Housekeeping;
using Mod.ManagementApi.Pki;
using Mod.ManagementApi.ServiceBus;
using Mod.Platform.Common.Certificates;
using Mod.Platform.Common.Registry;
using Mod.Platform.Common.Sql;

static string Req(string key) =>
    Environment.GetEnvironmentVariable(key)
    ?? throw new InvalidOperationException($"Required environment variable '{key}' is not set.");

static int Opt(string key, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;

var sqlConnection = Req("SQL_CONNECTION");
var kvUri = Req("KEYVAULT_URI");
var caCertName = Req("CA_CERT_NAME");
var certValidityDays = Opt("CERT_VALIDITY_DAYS", 7);
var cmdSbFqdn = Req("CMD_SB_FQDN");
var cmdSbSasKeyName = Req("CMD_SB_SAS_KEY_NAME");
var cmdSbSasKey = Req("CMD_SB_SAS_KEY");
var ingestUrl = Req("INGEST_URL");
var managementUrl = Req("MANAGEMENT_URL");
var cmdTokenLifetimeMinutes = Opt("COMMAND_TOKEN_LIFETIME_MINUTES", 60);
var caPfxPath = Environment.GetEnvironmentVariable("CA_PFX_PATH");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Mod.ManagementApi"))
    .UseAzureMonitor();

builder.Services.AddMemoryCache();

builder.Services.AddSingleton(new SqlConnectionFactory(sqlConnection));
builder.Services.AddSingleton<RegistryReader>();

var credential = new DefaultAzureCredential();
var secretClient = new SecretClient(new Uri(kvUri), credential);

builder.Services.AddSingleton(secretClient);
builder.Services.AddSingleton(new CertificateAuthority.Options(caCertName, caPfxPath, certValidityDays));
builder.Services.AddSingleton<CertificateAuthority>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<CertificateAuthority>());

// CertificateValidator needs the CA public cert; resolved after CA is loaded via hosted-service startup
builder.Services.AddSingleton<CertificateValidator>(sp =>
{
    var ca = sp.GetRequiredService<CertificateAuthority>();
    var caPem = ca.CaCertificatePem;
    var caCert = X509Certificate2.CreateFromPem(caPem);
    var registry = sp.GetRequiredService<RegistryReader>();
    return new CertificateValidator(caCert, registry);
});

builder.Services.AddSingleton(new SasTokenFactory.Options(
    cmdSbFqdn, cmdSbSasKeyName, cmdSbSasKey, cmdTokenLifetimeMinutes));
builder.Services.AddSingleton<SasTokenFactory>();

builder.Services.AddSingleton(new ConfigDocumentBuilder.Options(ingestUrl, managementUrl));
builder.Services.AddSingleton<ConfigDocumentBuilder>();

builder.Services.AddHostedService<HealthHistoryPurge>();

builder.Services.AddHealthChecks().AddCheck<SqlHealthCheck>("sql");

builder.Services.ConfigureHttpJsonOptions(opts =>
{
    opts.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    opts.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/readyz");

app.MapPost("/v1/enrol", EnrolEndpoint.HandleAsync);
app.MapGet("/v1/config", ConfigEndpoint.HandleAsync);
app.MapPost("/v1/health", HealthEndpoint.HandleAsync);
app.MapPost("/v1/certificate/renew", RenewEndpoint.HandleAsync);
app.MapGet("/v1/command-channel", CommandChannelEndpoint.HandleAsync);

app.Run();

internal sealed class SqlHealthCheck(SqlConnectionFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var conn = factory.CreateConnection();
            await conn.OpenAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message);
        }
    }
}
