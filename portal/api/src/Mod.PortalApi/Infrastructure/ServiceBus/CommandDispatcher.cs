using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Messaging.ServiceBus;
using Mod.PortalApi.Features.Admin.Models;
using Mod.PortalApi.Features.Admin.Services;

namespace Mod.PortalApi.Infrastructure.ServiceBus;

internal sealed class CommandDispatcher : ICommandDispatcher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly IReadOnlyDictionary<string, TimeSpan> Deadlines = new Dictionary<string, TimeSpan>
    {
        ["GetStats"]       = TimeSpan.FromSeconds(20),
        ["GetDiagnostics"] = TimeSpan.FromSeconds(30),
        ["ReloadConfig"]   = TimeSpan.FromSeconds(20),
        ["Restart"]        = TimeSpan.FromSeconds(20),
    };

    private static readonly IReadOnlyDictionary<string, TimeSpan> Ttls = new Dictionary<string, TimeSpan>
    {
        ["GetStats"]       = TimeSpan.FromSeconds(60),
        ["GetDiagnostics"] = TimeSpan.FromSeconds(60),
        ["ReloadConfig"]   = TimeSpan.FromSeconds(60),
        ["Restart"]        = TimeSpan.FromSeconds(60),
    };

    private readonly ServiceBusClient? _client;
    private readonly ICollectorService _collectorService;
    private readonly ILogger<CommandDispatcher> _logger;

    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<CommandReplyBody?>> _pending = new();
    private readonly ConcurrentDictionary<string, ReceiverSession> _receivers = new();

    public CommandDispatcher(
        ServiceBusClient? client,
        ICollectorService collectorService,
        ILogger<CommandDispatcher> logger)
    {
        _client = client;
        _collectorService = collectorService;
        _logger = logger;
    }

    public async Task<CommandResult> SendCommandAsync(
        Guid collectorId, Guid tenantId, string type, Actor actor)
    {
        if (_client is null)
            return new CommandResult(Guid.NewGuid(), "Failed", null, "Service Bus not configured", 0);

        if (!Deadlines.TryGetValue(type, out var deadline))
            return new CommandResult(Guid.NewGuid(), "Rejected", null, $"Unknown command type: {type}", 0);

        var requestId = Guid.NewGuid();
        var tcs = new TaskCompletionSource<CommandReplyBody?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        var replyQueue = QueueManager.ReplyQueueName(tenantId, collectorId);
        var requestQueue = QueueManager.RequestQueueName(tenantId, collectorId);

        EnsureReceiver(replyQueue);

        var sw = Stopwatch.StartNew();
        try
        {
            var issuedAt = DateTimeOffset.UtcNow;
            var expiresAt = issuedAt.Add(Ttls[type]);

            var body = JsonSerializer.SerializeToUtf8Bytes(new
            {
                requestId = requestId.ToString("D"),
                type,
                issuedAt = issuedAt.ToString("O"),
                expiresAt = expiresAt.ToString("O"),
                args = new { },
            }, JsonOpts);

            var sender = _client.CreateSender(requestQueue);
            await using (sender)
            {
                var msg = new ServiceBusMessage(body)
                {
                    MessageId = requestId.ToString("D"),
                    CorrelationId = requestId.ToString("D"),
                    ReplyTo = replyQueue,
                    TimeToLive = Ttls[type],
                    ContentType = "application/json",
                };
                msg.ApplicationProperties["mod.type"] = type;
                await sender.SendMessageAsync(msg);
            }

            CommandReplyBody? reply = null;
            try
            {
                reply = await tcs.Task.WaitAsync(deadline);
            }
            catch (TimeoutException)
            {
                _pending.TryRemove(requestId, out _);

                // Determine Unreachable vs TimedOut
                var info = await _collectorService.GetCollectorTenantInfoAsync(collectorId);
                var threshold = TimeSpan.FromSeconds((info?.healthIntervalSeconds ?? 60) * 2);
                // The health data on the collector tells us last seen; check via service
                // For PoC simplicity: if health interval info not available, use TimedOut
                return new CommandResult(requestId, "TimedOut", null, "No reply before deadline", sw.ElapsedMilliseconds);
            }

            _pending.TryRemove(requestId, out _);

            if (reply is null)
                return new CommandResult(requestId, "TimedOut", null, "No reply", sw.ElapsedMilliseconds);

            return new CommandResult(requestId, reply.Status, reply.Result, reply.Error, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(requestId, out _);
            _logger.LogError(ex, "Command dispatch error for collector {CollectorId}", collectorId);
            return new CommandResult(requestId, "Failed", null, ex.Message, sw.ElapsedMilliseconds);
        }
    }

    private void EnsureReceiver(string replyQueue)
    {
        _receivers.GetOrAdd(replyQueue, q =>
        {
            var cts = new CancellationTokenSource();
            var session = new ReceiverSession(_client!.CreateReceiver(q), DateTime.UtcNow, cts);
            _ = RunReceiverAsync(session, q);
            return session;
        });
    }

    private async Task RunReceiverAsync(ReceiverSession session, string queueName)
    {
        try
        {
            while (!session.Cts.Token.IsCancellationRequested)
            {
                ServiceBusReceivedMessage? msg;
                try
                {
                    msg = await session.Receiver.ReceiveMessageAsync(
                        maxWaitTime: TimeSpan.FromSeconds(10),
                        cancellationToken: session.Cts.Token);
                }
                catch (OperationCanceledException) { break; }

                if (msg is null)
                {
                    if (DateTime.UtcNow - session.LastActivity > TimeSpan.FromMinutes(2))
                        break;
                    continue;
                }

                session.LastActivity = DateTime.UtcNow;

                try
                {
                    if (Guid.TryParse(msg.CorrelationId, out var correlationId))
                    {
                        var reply = JsonSerializer.Deserialize<CommandReplyBody>(msg.Body, JsonOpts);
                        if (_pending.TryRemove(correlationId, out var tcs))
                            tcs.TrySetResult(reply);
                    }
                    await session.Receiver.CompleteMessageAsync(msg, session.Cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing reply message");
                }
            }
        }
        finally
        {
            _receivers.TryRemove(queueName, out _);
            await session.Receiver.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _receivers.Values)
            await session.Cts.CancelAsync();
    }

    private sealed class ReceiverSession(
        ServiceBusReceiver receiver,
        DateTime lastActivity,
        CancellationTokenSource cts)
    {
        public ServiceBusReceiver Receiver { get; } = receiver;
        public DateTime LastActivity { get; set; } = lastActivity;
        public CancellationTokenSource Cts { get; } = cts;
    }

    private sealed record CommandReplyBody(
        string RequestId,
        string Status,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Result,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error);
}
