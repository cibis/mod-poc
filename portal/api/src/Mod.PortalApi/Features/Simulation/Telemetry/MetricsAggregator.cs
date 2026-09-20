// DEMO SCAFFOLDING
using System.Text.Json;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.SignalR;
using Mod.PortalApi.Features.Simulation.Data;
using Mod.PortalApi.Features.Simulation.Hubs;
using Mod.PortalApi.Features.Simulation.Models;
using Mod.PortalApi.Features.Simulation.Provisioning;
using Mod.PortalApi.Features.Simulation.Telemetry;
using Mod.PortalApi.Infrastructure;

namespace Mod.PortalApi.Features.Simulation.Telemetry;

// Runs every 2 s. Gathers metrics from all sources, pushes metricsTick via SignalR,
// writes MetricSample to SQL every 10 s, purges old samples hourly, and pushes
// topologyChanged when the topology snapshot changes.
internal sealed class MetricsAggregator(
    SimStateCache stateCache,
    SimRepository repo,
    ContainerAppProvisioner? provisioner,
    IHubContext<SimHub> hub,
    ILogger<MetricsAggregator> logger) : BackgroundService
{
    // Event Hubs config
    private readonly string _ehFqdn = Env("EVENTHUB_FQDN");
    private readonly string _ehName = Env("EVENTHUB_NAME");
    private readonly string _ehConsumerGroup = Env("EVENTHUB_CONSUMER_GROUP", "processing");
    private readonly string _checkpointUrl = Env("CHECKPOINT_BLOB_CONTAINER_URL");
    // Production app names
    private readonly string _ingestApp = Env("INGEST_APP_NAME", "ca-ingest");
    private readonly string _processingApp = Env("PROCESSING_APP_NAME", "ca-processing");
    private readonly string _managementApp = Env("MANAGEMENT_APP_NAME", "ca-mgmt");
    private readonly string _portalApp = Env("PORTAL_APP_NAME", "ca-portal");
    private readonly string _reportingApp = Env("REPORTING_APP_NAME", "ca-reporting");

    // Rate-tracking (previous values for Δ computation)
    private long _prevEnqueuedTotal;
    private long _prevProcessedTotal;
    private long _prevDeadLetteredTotal;
    private DateTime _prevRateTime = DateTime.UtcNow;

    // SQL write throttle (every 10 s)
    private DateTime _lastSqlWrite = DateTime.MinValue;
    // Hourly purge
    private DateTime _lastPurge = DateTime.MinValue;

    // Topology change detection
    private string _lastTopologyJson = string.Empty;

    // Lazy-initialized Azure clients
    private EventHubProducerClient? _ehClient;
    private BlobContainerClient? _blobClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "MetricsAggregator tick error");
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var metrics = new Dictionary<string, double>();

        // ── In-memory sim state ─────────────────────────────────────────────
        var allStatuses = stateCache.GetAllStatuses();
        var collectorStates = await repo.GetAllCollectorStatesAsync();

        var poweredOn = collectorStates.Count(s => s.ProvisioningState is "Running" or "Provisioning");
        metrics["collectors.poweredOn"] = poweredOn;
        metrics["collectors.linkDown"] = allStatuses.Values.Count(s => s.LinkState == "down");

        foreach (var status in allStatuses.Values)
        {
            var id = status.CollectorId.ToString("D");
            metrics[$"collector.{id}.bufferDepth"] = status.BufferDepthEvents;
            metrics[$"collector.{id}.bufferCapacity"] = status.BufferCapacityEvents;
        }

        // ── Event Hubs lag and ingest rate ──────────────────────────────────
        if (!string.IsNullOrEmpty(_ehFqdn) && !string.IsNullOrEmpty(_ehName))
        {
            try
            {
                _ehClient ??= new EventHubProducerClient(_ehFqdn, _ehName, CredentialFactory.CreateDefault());
                var partitionIds = await _ehClient.GetPartitionIdsAsync(ct);
                long currentEnqueued = 0;
                foreach (var pid in partitionIds)
                {
                    var props = await _ehClient.GetPartitionPropertiesAsync(pid, ct);
                    currentEnqueued += props.LastEnqueuedSequenceNumber;
                }

                var elapsed = (now - _prevRateTime).TotalSeconds;
                if (elapsed > 0 && _prevEnqueuedTotal > 0)
                    metrics["ingest.batchesPerSec"] = (currentEnqueued - _prevEnqueuedTotal) / elapsed;

                // Lag: last enqueued − checkpoint per partition
                long checkpointTotal = await ReadCheckpointTotalAsync(partitionIds, ct);
                metrics["eventhub.lagBatches"] = Math.Max(0, currentEnqueued - checkpointTotal);

                _prevEnqueuedTotal = currentEnqueued;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read Event Hubs metrics");
            }
        }

        // ── ARM replica counts ──────────────────────────────────────────────
        if (provisioner is not null)
        {
            metrics["ingest.replicas"] = await provisioner.GetRunningReplicaCountAsync(_ingestApp, ct);
            metrics["processing.replicas"] = await provisioner.GetRunningReplicaCountAsync(_processingApp, ct);
            metrics["management.replicas"] = await provisioner.GetRunningReplicaCountAsync(_managementApp, ct);
            metrics["portal.replicas"] = await provisioner.GetRunningReplicaCountAsync(_portalApp, ct);
            metrics["reporting.replicas"] = await provisioner.GetRunningReplicaCountAsync(_reportingApp, ct);
        }

        // ── Processing rates from telemetry.CollectorFreshness ──────────────
        try
        {
            var (totalProcessed, totalDeadLettered) = await repo.GetFreshnessTotalsAsync();
            var elapsed = (now - _prevRateTime).TotalSeconds;
            if (elapsed > 0 && _prevProcessedTotal > 0)
            {
                metrics["processing.eventsPerSec"] =
                    (totalProcessed - _prevProcessedTotal) / elapsed;
                metrics["processing.deadLettersPerMin"] =
                    (totalDeadLettered - _prevDeadLetteredTotal) / elapsed * 60.0;
            }
            _prevProcessedTotal = totalProcessed;
            _prevDeadLetteredTotal = totalDeadLettered;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read freshness totals");
        }

        // ── Freshness age per powered-on collector ──────────────────────────
        try
        {
            var poweredIds = collectorStates
                .Where(s => s.ProvisioningState is "Running" or "Provisioning")
                .Select(s => s.CollectorId).ToList();
            if (poweredIds.Count > 0)
            {
                var ages = await repo.GetFreshnessAgesAsync(poweredIds);
                foreach (var (id, age) in ages)
                    metrics[$"collector.{id:D}.freshnessAgeSec"] = age;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read freshness ages");
        }

        // ── Rollup restatements ─────────────────────────────────────────────
        try
        {
            metrics["rollups.restatedLastHour"] = await repo.GetRestatedRollupCountLastHourAsync();
        }
        catch { /* non-critical */ }

        // ── Tenant completeness ─────────────────────────────────────────────
        try
        {
            var completeness = await repo.GetTenantCompletenessAsync();
            foreach (var (tenantId, value) in completeness)
                metrics[$"tenant.{tenantId:D}.completenessLastHour"] = value;
        }
        catch { /* non-critical */ }

        _prevRateTime = now;

        // ── Push metricsTick hub event ──────────────────────────────────────
        await hub.Clients.All.SendAsync("metricsTick",
            new { at = now, values = metrics }, ct);

        // ── Write MetricSample to SQL every 10 s ────────────────────────────
        if ((now - _lastSqlWrite).TotalSeconds >= 10)
        {
            _lastSqlWrite = now;
            var samples = metrics.Select(kv => (kv.Key, kv.Value));
            try { await repo.InsertMetricSamplesAsync(samples); }
            catch (Exception ex) { logger.LogDebug(ex, "Could not write metric samples"); }
        }

        // ── Purge old samples hourly ─────────────────────────────────────────
        if ((now - _lastPurge).TotalHours >= 1)
        {
            _lastPurge = now;
            try { await repo.PurgeOldMetricSamplesAsync(); }
            catch (Exception ex) { logger.LogDebug(ex, "Could not purge metric samples"); }
        }

        // ── Topology ────────────────────────────────────────────────────────
        var replicaCounts = new Dictionary<string, int>();
        if (metrics.TryGetValue("ingest.replicas", out var ir)) replicaCounts["ingest"] = (int)ir;
        if (metrics.TryGetValue("processing.replicas", out var pr)) replicaCounts["processing"] = (int)pr;
        if (metrics.TryGetValue("management.replicas", out var mr)) replicaCounts["management"] = (int)mr;
        if (metrics.TryGetValue("portal.replicas", out var por)) replicaCounts["portal"] = (int)por;
        if (metrics.TryGetValue("reporting.replicas", out var rr)) replicaCounts["reporting"] = (int)rr;

        var topology = TopologyBuilder.Build(collectorStates, allStatuses, replicaCounts);
        var topologyJson = JsonSerializer.Serialize(topology);
        if (topologyJson != _lastTopologyJson)
        {
            _lastTopologyJson = topologyJson;
            await hub.Clients.All.SendAsync("topologyChanged", topology, ct);
        }
    }

    private async Task<long> ReadCheckpointTotalAsync(string[] partitionIds, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_checkpointUrl)) return 0;

        try
        {
            _blobClient ??= new BlobContainerClient(
                new Uri(_checkpointUrl), CredentialFactory.CreateDefault());

            long total = 0;
            var prefix = $"{_ehFqdn.ToLowerInvariant()}/{_ehName.ToLowerInvariant()}/{_ehConsumerGroup.ToLowerInvariant()}/";
            foreach (var pid in partitionIds)
            {
                var blobName = prefix + pid;
                var blob = _blobClient.GetBlobClient(blobName);
                try
                {
                    var props = await blob.GetPropertiesAsync(cancellationToken: ct);
                    if (props.Value.Metadata.TryGetValue("sequencenumber", out var seqStr) &&
                        long.TryParse(seqStr, out var seq))
                    {
                        total += seq;
                    }
                    else
                    {
                        // Try reading blob content as JSON.
                        var download = await blob.DownloadContentAsync(ct);
                        using var doc = JsonDocument.Parse(download.Value.Content);
                        if (doc.RootElement.TryGetProperty("SequenceNumber", out var seqProp) ||
                            doc.RootElement.TryGetProperty("sequencenumber", out seqProp))
                        {
                            total += seqProp.GetInt64();
                        }
                    }
                }
                catch { /* missing checkpoint = 0 contribution */ }
            }
            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static string Env(string name, string def = "") =>
        Environment.GetEnvironmentVariable(name) ?? def;
}
