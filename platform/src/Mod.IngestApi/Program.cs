using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Azure.Identity;
using Azure.Messaging.EventHubs.Producer;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mod.Platform.Common.Certificates;
using Mod.Platform.Common.Registry;
using Mod.Platform.Common.Sql;
using Mod.IngestApi.Endpoints;
using Mod.IngestApi.EventHub;

// Fail-fast: require every mandatory environment variable at startup
static string Req(string key) =>
    Environment.GetEnvironmentVariable(key)
    ?? throw new InvalidOperationException($"Required environment variable '{key}' is not set.");

static int Opt(string key, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;

var sqlConnection = Req("SQL_CONNECTION");
var eventHubFqdn = Req("EVENTHUB_FQDN");
var eventHubName = Req("EVENTHUB_NAME");
var caCertPemBase64 = Req("COLLECTOR_CA_CERT_PEM_BASE64");
var maxEvents = Opt("INGEST_MAX_EVENTS", 500);
var maxBytes = Opt("INGEST_MAX_BYTES", 262144);
var rateLimit = Opt("INGEST_RATE_LIMIT_PER_COLLECTOR", 20);

var caCert = X509Certificate2.CreateFromPem(
    System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(caCertPemBase64)));

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry — custom meter + Azure Monitor exporter
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Mod.IngestApi"))
    .UseAzureMonitor();

// Memory cache (registry lookups ≤ 30 s)
builder.Services.AddMemoryCache();

// SQL + registry reader
builder.Services.AddSingleton(new SqlConnectionFactory(sqlConnection));
builder.Services.AddSingleton<RegistryReader>();

// Certificate validation (custom CA trust, registry revocation check)
builder.Services.AddSingleton(caCert);
builder.Services.AddSingleton<CertificateValidator>();

// Event Hubs producer (one per process)
builder.Services.AddSingleton(
    new EventHubProducerClient(eventHubFqdn, eventHubName, new DefaultAzureCredential()));

// Token-bucket rate limiter keyed by collectorId
var rateLimiter = PartitionedRateLimiter.Create<string, string>(
    key => RateLimitPartition.GetTokenBucketLimiter(
        key,
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = rateLimit,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = rateLimit,
            AutoReplenishment = true,
            QueueLimit = 0
        }));
builder.Services.AddSingleton<PartitionedRateLimiter<string>>(rateLimiter);

// Batch publisher + endpoint options
builder.Services.AddSingleton<BatchPublisher>();
builder.Services.AddSingleton(new BatchEndpoint.Options(maxEvents, maxBytes));

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<SqlHealthCheck>("sql");

var app = builder.Build();

// Liveness: no dependency probes
app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });
// Readiness: run all registered checks
app.MapHealthChecks("/readyz");

app.MapPost("/v1/batches", BatchEndpoint.HandleAsync);

app.Run();

// -- Health check -------------------------------------------------------

internal sealed class SqlHealthCheck(SqlConnectionFactory factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken ct = default)
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
