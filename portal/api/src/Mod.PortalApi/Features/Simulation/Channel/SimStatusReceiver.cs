// DEMO SCAFFOLDING
using System.Collections.Concurrent;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.SignalR;
using Mod.PortalApi.Features.Simulation.Data;
using Mod.PortalApi.Features.Simulation.Hubs;
using Mod.PortalApi.Features.Simulation.Models;

namespace Mod.PortalApi.Features.Simulation.Channel;

// Receives sim-status messages from all collectors, updates the in-memory cache,
// pushes collectorStatus hub events, and persists LastStatusJson throttled to 10 s per collector.
internal sealed class SimStatusReceiver(
    SimServiceBusClient sbClient,
    SimStateCache stateCache,
    SimRepository repo,
    IHubContext<SimHub> hub,
    ILogger<SimStatusReceiver> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions _writeOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ConcurrentDictionary<Guid, DateTime> _lastSqlWrite = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var processor = sbClient.GetClient().CreateProcessor("sim-status",
            new ServiceBusProcessorOptions { MaxConcurrentCalls = 4 });

        processor.ProcessMessageAsync += HandleMessageAsync;
        processor.ProcessErrorAsync += HandleErrorAsync;

        await processor.StartProcessingAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await processor.StopProcessingAsync();
            await processor.DisposeAsync();
        }
    }

    private async Task HandleMessageAsync(ProcessMessageEventArgs args)
    {
        try
        {
            var status = JsonSerializer.Deserialize<SimStatusBody>(
                args.Message.Body.ToString(), _jsonOpts);

            if (status is null)
            {
                await args.CompleteMessageAsync(args.Message);
                return;
            }

            var now = DateTime.UtcNow;
            stateCache.UpdateStatus(status.CollectorId, status, now);

            await hub.Clients.All.SendAsync("collectorStatus",
                new { collectorId = status.CollectorId, status });

            if (status.BufferOverflowEver && !stateCache.GetBufferOverflowEver(status.CollectorId))
                _ = repo.SetBufferOverflowEverAsync(status.CollectorId);

            if (!_lastSqlWrite.TryGetValue(status.CollectorId, out var lastWrite) ||
                (now - lastWrite).TotalSeconds >= 10)
            {
                _lastSqlWrite[status.CollectorId] = now;
                var json = JsonSerializer.Serialize(status, _writeOpts);
                _ = repo.UpdateLastStatusAsync(status.CollectorId, json, now);
            }

            await args.CompleteMessageAsync(args.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Error processing sim-status message");
            await args.AbandonMessageAsync(args.Message);
        }
    }

    private Task HandleErrorAsync(ProcessErrorEventArgs args)
    {
        logger.LogError(args.Exception, "Sim-status processor error: {Source}", args.ErrorSource);
        return Task.CompletedTask;
    }
}
