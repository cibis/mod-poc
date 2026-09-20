using System.Collections.Concurrent;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Processor;
using Polly.CircuitBreaker;
using Mod.Processing.Pipeline;
using Microsoft.Extensions.Logging;

namespace Mod.Processing.Hosting;

internal sealed class ProcessorHost(
    EventProcessorClient processor,
    BatchProcessor batchProcessor,
    LeaderState leaderState,
    ILogger<ProcessorHost> logger) : BackgroundService
{
    private const int CheckpointEventThreshold = 50;
    private static readonly TimeSpan CheckpointTimeThreshold = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, PartitionState> _partitions = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        processor.PartitionInitializingAsync += OnPartitionInitializing;
        processor.PartitionClosingAsync += OnPartitionClosing;
        processor.ProcessEventAsync += args => OnProcessEventAsync(args, stoppingToken);
        processor.ProcessErrorAsync += OnProcessErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }

        await processor.StopProcessingAsync(CancellationToken.None);

        // Checkpoint whatever was last committed on shutdown
        foreach (var (_, state) in _partitions)
        {
            if (state.LastArgs is { } args)
            {
                try { await args.UpdateCheckpointAsync(CancellationToken.None); }
                catch (Exception ex) { logger.LogWarning(ex, "Shutdown checkpoint failed"); }
            }
        }
    }

    private Task OnPartitionInitializing(PartitionInitializingEventArgs args)
    {
        _partitions[args.PartitionId] = new PartitionState();
        if (args.PartitionId == "0")
        {
            leaderState.Set(true);
            logger.LogInformation("Acquired partition 0 — this replica is leader");
        }
        return Task.CompletedTask;
    }

    private Task OnPartitionClosing(PartitionClosingEventArgs args)
    {
        _partitions.TryRemove(args.PartitionId, out _);
        if (args.PartitionId == "0")
        {
            leaderState.Set(false);
            logger.LogInformation("Released partition 0 — this replica is no longer leader");
        }
        return Task.CompletedTask;
    }

    private async Task OnProcessEventAsync(ProcessEventArgs args, CancellationToken stoppingToken)
    {
        if (!args.HasEvent) return;

        var state = _partitions.GetOrAdd(args.Partition.PartitionId, _ => new PartitionState());

        try
        {
            await batchProcessor.ProcessAsync(args.Data, stoppingToken);

            state.LastArgs = args;
            state.EventCount++;

            var shouldCheckpoint = state.EventCount >= CheckpointEventThreshold
                || DateTime.UtcNow - state.LastCheckpointAt >= CheckpointTimeThreshold;

            if (shouldCheckpoint)
            {
                await args.UpdateCheckpointAsync(stoppingToken);
                state.EventCount = 0;
                state.LastCheckpointAt = DateTime.UtcNow;
                state.LastArgs = null;
            }
        }
        catch (BrokenCircuitException)
        {
            // Circuit open: do not checkpoint so events are reprocessed after circuit closes
            logger.LogWarning(
                "SQL circuit open on partition {PartitionId} — delaying 30 s without checkpoint",
                args.Partition.PartitionId);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unhandled error on partition {PartitionId} batchId={BatchId} — not checkpointing",
                args.Partition.PartitionId,
                args.Data.Properties.TryGetValue("mod.batchId", out var bid) ? bid : "?");
        }
    }

    private Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception,
            "Event Hubs processor error on partition {PartitionId} operation {Operation}",
            args.PartitionId, args.Operation);
        return Task.CompletedTask;
    }

    private sealed class PartitionState
    {
        public ProcessEventArgs? LastArgs;
        public int EventCount;
        public DateTime LastCheckpointAt = DateTime.UtcNow;
    }
}
