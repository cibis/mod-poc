using System.Buffers;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Messaging.ServiceBus;
using Mod.Collector.Buffer;
using Mod.Collector.Config;
using Mod.Collector.Forwarding;
using Mod.Collector.Health;
using Mod.Collector.Identity;
using Mod.Collector.Network;

namespace Mod.Collector.Commands;

internal sealed class ProductCommandListener : BackgroundService
{
    private readonly ConfigService _config;
    private readonly IdentityStore _identity;
    private readonly LinkGate _linkGate;
    private readonly SqliteBuffer _buffer;
    private readonly Forwarder _forwarder;
    private readonly CollectorOptions _options;
    private readonly LogCapture _logCapture;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ProductCommandListener> _logger;

    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public ProductCommandListener(
        ConfigService config,
        IdentityStore identity,
        LinkGate linkGate,
        SqliteBuffer buffer,
        Forwarder forwarder,
        CollectorOptions options,
        LogCapture logCapture,
        IHostApplicationLifetime lifetime,
        IHttpClientFactory httpFactory,
        ILogger<ProductCommandListener> logger)
    {
        _config = config;
        _identity = identity;
        _linkGate = linkGate;
        _buffer = buffer;
        _forwarder = forwarder;
        _options = options;
        _logCapture = logCapture;
        _lifetime = lifetime;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _config.EnrolledTask.WaitAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cfg = _config.CurrentConfig;
            if (cfg is null || !cfg.CommandChannelEnabled)
            {
                await Task.Delay(5_000, stoppingToken);
                continue;
            }

            try
            {
                await RunSessionAsync(cfg.ManagementUrl, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Command channel session ended — will reconnect in 10 s");
                await Task.Delay(10_000, stoppingToken);
            }
        }
    }

    private async Task RunSessionAsync(string managementUrl, CancellationToken ct)
    {
        var tokens = await FetchTokensAsync(managementUrl, ct);
        if (tokens is null) return;

        await using var recvClient = new ServiceBusClient(tokens.NamespaceFqdn, new AzureSasCredential(tokens.RequestSasToken));
        await using var sendClient = new ServiceBusClient(tokens.NamespaceFqdn, new AzureSasCredential(tokens.ReplySasToken));
        await using var receiver = recvClient.CreateReceiver(tokens.RequestQueue,
            new ServiceBusReceiverOptions { ReceiveMode = ServiceBusReceiveMode.PeekLock });
        await using var sender = sendClient.CreateSender(tokens.ReplyQueue);

        _logger.LogInformation("Command channel connected to {Queue}", tokens.RequestQueue);

        while (!ct.IsCancellationRequested)
        {
            // Refresh tokens before they expire (< 10 min remaining).
            if (tokens.ExpiresAt - DateTimeOffset.UtcNow < TimeSpan.FromMinutes(10))
            {
                tokens = await FetchTokensAsync(_config.CurrentConfig?.ManagementUrl ?? managementUrl, ct);
                if (tokens is null) return;
            }

            // Check link gate — close session on link-down.
            if (!_linkGate.IsUp)
            {
                _logger.LogInformation("Link down — pausing command channel");
                await WaitForLinkUpAsync(ct);
                // Reopen session with fresh tokens.
                return;
            }

            ServiceBusReceivedMessage? msg;
            try
            {
                msg = await receiver.ReceiveMessageAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (ServiceBusException sbEx) when (sbEx.IsTransient)
            {
                await Task.Delay(1_000, ct);
                continue;
            }

            if (msg is null) continue;

            await ProcessMessageAsync(msg, receiver, sender, ct);
        }
    }

    private async Task ProcessMessageAsync(
        ServiceBusReceivedMessage msg,
        ServiceBusReceiver receiver,
        ServiceBusSender sender,
        CancellationToken ct)
    {
        string requestId;
        string type;
        DateTimeOffset expiresAt;
        JsonElement args;

        try
        {
            using var doc = JsonDocument.Parse(msg.Body);
            var root = doc.RootElement;
            requestId = root.GetProperty("requestId").GetString()!;
            type = root.GetProperty("type").GetString()!;
            expiresAt = root.GetProperty("expiresAt").GetDateTimeOffset();
            args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse command message — abandoning");
            await receiver.AbandonMessageAsync(msg, cancellationToken: ct);
            return;
        }

        // Expired: complete without processing.
        if (DateTimeOffset.UtcNow > expiresAt)
        {
            _logger.LogInformation("Command {RequestId} expired — completing without reply", requestId);
            await receiver.CompleteMessageAsync(msg, ct);
            return;
        }

        // Idempotency check.
        var storedReply = await _buffer.GetCommandReplyAsync(requestId, ct);
        if (storedReply is not null)
        {
            await SendReplyAsync(sender, msg.CorrelationId ?? requestId, storedReply, ct);
            await receiver.CompleteMessageAsync(msg, ct);
            return;
        }

        // Dispatch.
        string replyJson;
        bool restart = false;
        try
        {
            (replyJson, restart) = await DispatchAsync(requestId, type, args, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Command {Type} execution failed", type);
            replyJson = BuildReply(requestId, "Failed", null, ex.Message);
        }

        await _buffer.SaveCommandReplyAsync(requestId, replyJson, ct);
        await SendReplyAsync(sender, msg.CorrelationId ?? requestId, replyJson, ct);
        await receiver.CompleteMessageAsync(msg, ct);

        if (restart)
        {
            _logger.LogInformation("Restart command received — stopping host");
            _lifetime.StopApplication();
        }
    }

    private async Task<(string replyJson, bool restart)> DispatchAsync(
        string requestId, string type, JsonElement args, CancellationToken ct)
    {
        switch (type)
        {
            case "GetStats":
            {
                var cfg = _config.CurrentConfig;
                var depth = await _buffer.CountAsync(ct);
                var oldest = await _buffer.GetOldestEventTimeAsync(ct);
                var sourcesEl = _config.CurrentSources.Select(s => new
                {
                    sourceId = s.SourceId,
                    lastReadAt = s.LastReadAt == DateTimeOffset.MinValue
                        ? (string?)null : Ts(s.LastReadAt),
                    lastSequence = s.LastSequence,
                });
                var result = new
                {
                    bufferDepthEvents = depth,
                    bufferCapacityEvents = cfg?.CapacityEvents ?? 0,
                    oldestBufferedEventTime = oldest.HasValue ? Ts(oldest.Value) : null,
                    forwardRateEventsPerSecond = _forwarder.ForwardRateEventsPerSecond,
                    lastAckAt = _forwarder.LastAckAt.HasValue ? Ts(_forwarder.LastAckAt.Value) : null,
                    sources = sourcesEl,
                };
                return (BuildReply(requestId, "Succeeded", result, null), false);
            }

            case "GetDiagnostics":
            {
                var cfg = _config.CurrentConfig;
                var result = new
                {
                    softwareVersion = _options.CollectorSoftwareVersion,
                    configVersion = cfg?.ConfigVersion ?? 0,
                    uptimeSeconds = (long)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds,
                    recentLogLines = _logCapture.GetLines(200),
                };
                return (BuildReply(requestId, "Succeeded", result, null), false);
            }

            case "ReloadConfig":
            {
                var newVersion = await _config.ForceConfigRefreshAsync(ct);
                return (BuildReply(requestId, "Succeeded", new { configVersion = newVersion }, null), false);
            }

            case "Restart":
                return (BuildReply(requestId, "Succeeded", null, null), true);

            default:
                return (BuildReply(requestId, "Rejected", null, $"Unknown command type: {type}"), false);
        }
    }

    private static async Task SendReplyAsync(
        ServiceBusSender sender, string correlationId, string replyJson, CancellationToken ct)
    {
        var outMsg = new ServiceBusMessage(Encoding.UTF8.GetBytes(replyJson))
        {
            CorrelationId = correlationId,
            ContentType = "application/json",
        };
        await sender.SendMessageAsync(outMsg, ct);
    }

    private static string BuildReply(string requestId, string status, object? result, string? error)
    {
        var buf = new ArrayBufferWriter<byte>();
        using var w = new Utf8JsonWriter(buf);
        w.WriteStartObject();
        w.WriteString("requestId", requestId);
        w.WriteString("status", status);
        w.WriteString("completedAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        if (result is not null)
        {
            w.WritePropertyName("result");
            JsonSerializer.Serialize(w, result);
        }
        else
            w.WriteNull("result");
        if (error is not null)
            w.WriteString("error", error);
        else
            w.WriteNull("error");
        w.WriteEndObject();
        w.Flush();
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private async Task<CommandChannelTokens?> FetchTokensAsync(string managementUrl, CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            using var response = await client.GetAsync(
                $"{managementUrl.TrimEnd('/')}/v1/command-channel", ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("enabled", out var enabledEl) || !enabledEl.GetBoolean())
            {
                _logger.LogInformation("Command channel disabled on server");
                return null;
            }

            return new CommandChannelTokens(
                root.GetProperty("namespaceFqdn").GetString()!,
                root.GetProperty("requestQueue").GetString()!,
                root.GetProperty("replyQueue").GetString()!,
                root.GetProperty("requestSasToken").GetString()!,
                root.GetProperty("replySasToken").GetString()!,
                root.GetProperty("expiresAt").GetDateTimeOffset());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch command-channel tokens");
            return null;
        }
    }

    private async Task WaitForLinkUpAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && !_linkGate.IsUp)
            await Task.Delay(1_000, ct);
    }

    private static string Ts(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}

internal sealed record CommandChannelTokens(
    string NamespaceFqdn,
    string RequestQueue,
    string ReplyQueue,
    string RequestSasToken,
    string ReplySasToken,
    DateTimeOffset ExpiresAt);
