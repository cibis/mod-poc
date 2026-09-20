using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Mod.Collector.Buffer;
using Mod.Collector.Config;
using Mod.Collector.Network;

namespace Mod.Collector.Forwarding;

internal sealed class Forwarder : BackgroundService
{
    private readonly SqliteBuffer _buffer;
    private readonly LinkGate _linkGate;
    private readonly IHttpClientFactory _httpFactory;
    private readonly CollectorOptions _options;
    private readonly ConfigService _configService;
    private readonly ILogger<Forwarder> _logger;

    private DateTimeOffset _lastSendTime = DateTimeOffset.MinValue;

    // Metrics read by HealthReporter, ProductCommandListener, SimStatusPublisher
    private long _ackedTotal;
    private long _rejectedBatchesTotal;
    public long AckedTotal => Interlocked.Read(ref _ackedTotal);
    public long RejectedBatchesTotal => Interlocked.Read(ref _rejectedBatchesTotal);
    public DateTimeOffset? LastAckAt { get; private set; }

    // Rolling rate: events acked in the last ~10 s
    private readonly ConcurrentQueue<(DateTimeOffset time, int count)> _recentAcks = new();
    public double ForwardRateEventsPerSecond
    {
        get
        {
            var cutoff = DateTimeOffset.UtcNow.AddSeconds(-10);
            var total = 0L;
            foreach (var (t, c) in _recentAcks)
                if (t >= cutoff) total += c;
            return total / 10.0;
        }
    }

    public Forwarder(
        SqliteBuffer buffer,
        LinkGate linkGate,
        IHttpClientFactory httpFactory,
        CollectorOptions options,
        ConfigService configService,
        ILogger<Forwarder> logger)
    {
        _buffer = buffer;
        _linkGate = linkGate;
        _httpFactory = httpFactory;
        _options = options;
        _configService = configService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? batchId = null;
        List<BufferedEvent>? pendingEvents = null;
        int retryAttempt = 0;
        DateTimeOffset? oldestUnsent = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = _configService.CurrentConfig;
            if (config is null)
            {
                await Task.Delay(1_000, stoppingToken);
                continue;
            }

            if (pendingEvents is null)
            {
                var events = await _buffer.PeekBatchAsync(config.MaxEvents, config.MaxBytes, stoppingToken);
                if (events.Count == 0)
                {
                    oldestUnsent = null;
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                oldestUnsent ??= DateTimeOffset.UtcNow;

                var totalBytes = events.Sum(e => e.SizeBytes);
                var batchFull = events.Count >= config.MaxEvents || totalBytes >= config.MaxBytes;
                var elapsed = (DateTimeOffset.UtcNow - oldestUnsent.Value).TotalMilliseconds;
                var flushDue = elapsed >= config.FlushIntervalMs;

                if (!batchFull && !flushDue)
                {
                    await Task.Delay(100, stoppingToken);
                    continue;
                }

                batchId = Guid.NewGuid().ToString();
                pendingEvents = events;
                retryAttempt = 0;
            }

            await EnforceRateLimitAsync(config.MaxBatchesPerSecond, stoppingToken);

            try
            {
                _linkGate.Check();
            }
            catch (NetworkUnavailableException)
            {
                retryAttempt++;
                await Task.Delay(Backoff(retryAttempt, config.RetryBaseMs, config.RetryMaxMs), stoppingToken);
                continue;
            }

            var sentAt = DateTimeOffset.UtcNow;
            HttpResponseMessage? response = null;
            try
            {
                var body = BuildBatch(batchId!, _options.CollectorId, pendingEvents);
                var compressed = Compress(body);

                var content = new ByteArrayContent(compressed);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Headers.TryAddWithoutValidation("Content-Encoding", "gzip");

                var client = _httpFactory.CreateClient();
                response = await client.PostAsync(
                    $"{config.IngestUrl.TrimEnd('/')}/v1/batches", content, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Network error sending batch {BatchId}", batchId);
                await _buffer.RecordExecutedRequestAsync(batchId!, sentAt, null, false, stoppingToken);
                retryAttempt++;
                await Task.Delay(Backoff(retryAttempt, config.RetryBaseMs, config.RetryMaxMs), stoppingToken);
                continue;
            }

            await _buffer.RecordExecutedRequestAsync(batchId!, sentAt, (int)response.StatusCode,
                response.IsSuccessStatusCode, stoppingToken);

            switch (response.StatusCode)
            {
                case HttpStatusCode.Accepted:
                    _logger.LogInformation("Batch {BatchId} accepted ({Count} events)", batchId, pendingEvents.Count);
                    Interlocked.Add(ref _ackedTotal, pendingEvents.Count);
                    LastAckAt = DateTimeOffset.UtcNow;
                    _recentAcks.Enqueue((LastAckAt.Value, pendingEvents.Count));
                    // Trim stale entries
                    var cutoff = DateTimeOffset.UtcNow.AddSeconds(-12);
                    while (_recentAcks.TryPeek(out var head) && head.time < cutoff)
                        _recentAcks.TryDequeue(out _);
                    await _buffer.DeleteBatchAsync(pendingEvents.Select(e => e.RowId), stoppingToken);
                    batchId = null;
                    pendingEvents = null;
                    oldestUnsent = null;
                    retryAttempt = 0;
                    break;

                case HttpStatusCode.BadRequest:
                    _logger.LogWarning("Batch {BatchId} rejected (400 {Code}) — moving to rejected store", batchId,
                        TryReadProblemType(response));
                    Interlocked.Increment(ref _rejectedBatchesTotal);
                    await _buffer.RecordRejectedBatchAsync(batchId!, TryReadProblemType(response),
                        pendingEvents.Count, BuildBatchJson(batchId!, _options.CollectorId, pendingEvents),
                        stoppingToken);
                    await _buffer.DeleteBatchAsync(pendingEvents.Select(e => e.RowId), stoppingToken);
                    batchId = null;
                    pendingEvents = null;
                    oldestUnsent = null;
                    retryAttempt = 0;
                    break;

                case HttpStatusCode.RequestEntityTooLarge:
                    _logger.LogWarning("Batch {BatchId} returned 413 — splitting in half", batchId);
                    pendingEvents = pendingEvents.Take(Math.Max(1, pendingEvents.Count / 2)).ToList();
                    batchId = Guid.NewGuid().ToString();
                    retryAttempt = 0;
                    break;

                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.UnsupportedMediaType:
                    retryAttempt++;
                    var longDelay = Backoff(Math.Max(retryAttempt, 5), config.RetryBaseMs, config.RetryMaxMs);
                    _logger.LogWarning("Batch {BatchId}: {Status} — back-off {Ms} ms", batchId,
                        response.StatusCode, longDelay.TotalMilliseconds);
                    await Task.Delay(longDelay, stoppingToken);
                    break;

                case HttpStatusCode.TooManyRequests:
                case HttpStatusCode.ServiceUnavailable:
                    var retryAfter = RetryAfterDelay(response, config.RetryMaxMs);
                    _logger.LogDebug("Batch {BatchId}: {Status} — waiting {Ms} ms", batchId,
                        response.StatusCode, retryAfter.TotalMilliseconds);
                    await Task.Delay(retryAfter, stoppingToken);
                    retryAttempt++;
                    break;

                default:
                    retryAttempt++;
                    await Task.Delay(Backoff(retryAttempt, config.RetryBaseMs, config.RetryMaxMs), stoppingToken);
                    break;
            }
        }
    }

    private async Task EnforceRateLimitAsync(double maxBatchesPerSecond, CancellationToken ct)
    {
        if (maxBatchesPerSecond <= 0) return;
        var minIntervalMs = 1000.0 / maxBatchesPerSecond;
        var sinceMs = (DateTimeOffset.UtcNow - _lastSendTime).TotalMilliseconds;
        if (sinceMs < minIntervalMs)
            await Task.Delay((int)(minIntervalMs - sinceMs), ct);
        _lastSendTime = DateTimeOffset.UtcNow;
    }

    private static byte[] BuildBatch(string batchId, string collectorId, List<BufferedEvent> events)
        => Encoding.UTF8.GetBytes(BuildBatchJson(batchId, collectorId, events));

    private static string BuildBatchJson(string batchId, string collectorId, List<BufferedEvent> events)
    {
        var buf = new ArrayBufferWriter<byte>();
        using var w = new Utf8JsonWriter(buf);
        w.WriteStartObject();
        w.WriteString("batchId", batchId);
        w.WriteString("collectorId", collectorId);
        w.WriteString("schemaVersion", "1.0");
        w.WriteString("sentAt", DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
        w.WritePropertyName("events");
        w.WriteStartArray();
        foreach (var ev in events)
            w.WriteRawValue(ev.Json);
        w.WriteEndArray();
        w.WriteEndObject();
        w.Flush();
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static byte[] Compress(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Fastest))
            gz.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static TimeSpan Backoff(int attempt, int baseMs, int maxMs)
    {
        var cap = Math.Min(baseMs * Math.Pow(2, attempt - 1), maxMs);
        return TimeSpan.FromMilliseconds(Random.Shared.NextDouble() * cap);
    }

    private static TimeSpan RetryAfterDelay(HttpResponseMessage response, int maxMs)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta < TimeSpan.FromMilliseconds(maxMs) ? delta : TimeSpan.FromMilliseconds(maxMs);
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero && wait < TimeSpan.FromMilliseconds(maxMs)) return wait;
        }
        return TimeSpan.FromMilliseconds(maxMs);
    }

    private static string TryReadProblemType(HttpResponseMessage response)
    {
        try
        {
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("type", out var t)) return t.GetString() ?? "unknown";
        }
        catch { /* best-effort */ }
        return "unknown";
    }
}
