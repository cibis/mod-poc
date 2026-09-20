using Azure.Identity;
using Azure.Messaging.EventHubs;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Mod.Platform.Common.Sql;
using Mod.Processing;
using Mod.Processing.Hosting;
using Mod.Processing.Pipeline;
using Mod.Processing.Replay;
using Mod.Processing.Housekeeping;

static string Req(string key) =>
    Environment.GetEnvironmentVariable(key)
    ?? throw new InvalidOperationException($"Required environment variable '{key}' is not set.");

static int Opt(string key, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(key), out var v) ? v : fallback;

var sqlConnection = Req("SQL_CONNECTION");
var eventHubFqdn = Req("EVENTHUB_FQDN");
var eventHubName = Req("EVENTHUB_NAME");
var consumerGroup = Req("EVENTHUB_CONSUMER_GROUP");
var checkpointBlobUrl = Req("CHECKPOINT_BLOB_CONTAINER_URL");

var processingOpts = new ProcessingOptions(
    WindowGraceSeconds: Opt("WINDOW_GRACE_SECONDS", 120),
    ProcessingDelayMs: Opt("PROCESSING_DELAY_MS", 20),
    MappingCacheSeconds: Opt("MAPPING_CACHE_SECONDS", 30));

var retentionOpts = new RetentionOptions(
    DedupeRetentionDays: Opt("DEDUPE_RETENTION_DAYS", 14),
    RawRetentionDays: Opt("RAW_RETENTION_DAYS", 7));

var credential = new DefaultAzureCredential();

var checkpointContainer = new BlobContainerClient(new Uri(checkpointBlobUrl), credential);
var processorClient = new EventProcessorClient(
    checkpointContainer,
    consumerGroup,
    eventHubFqdn,
    eventHubName,
    credential);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter("Mod.Processing"))
    .UseAzureMonitor();

builder.Services.AddMemoryCache();

builder.Services.AddSingleton(new SqlConnectionFactory(sqlConnection));
builder.Services.AddSingleton(processorClient);
builder.Services.AddSingleton(processingOpts);
builder.Services.AddSingleton(retentionOpts);
builder.Services.AddSingleton<LeaderState>();
builder.Services.AddSingleton<MappingCache>();
builder.Services.AddSingleton<SqlWriter>();
builder.Services.AddSingleton<BatchProcessor>();
builder.Services.AddHostedService<ProcessorHost>();
builder.Services.AddHostedService<DeadLetterReplayService>();
builder.Services.AddHostedService<RetentionService>();

builder.Services.AddHealthChecks()
    .AddCheck<SqlHealthCheck>("sql");

var app = builder.Build();

app.MapHealthChecks("/healthz", new HealthCheckOptions { Predicate = _ => false });

app.Run();

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
