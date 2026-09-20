using System.Buffers;
using System.Text;
using System.Text.Json;
using Mod.Collector.Buffer;
using Mod.Collector.Config;
using Mod.Collector.Forwarding;
using Mod.Collector.Identity;
using Mod.Collector.Models;
using Mod.Collector.Network;
using Mod.Collector.Sources;

namespace Mod.Collector.Health;

internal sealed class HealthReporter : BackgroundService
{
    private readonly ConfigService _config;
    private readonly SqliteBuffer _buffer;
    private readonly IdentityStore _identity;
    private readonly CollectorOptions _options;
    private readonly MinuteCounter _minute;
    private readonly LinkGate _linkGate;
    private readonly Forwarder _forwarder;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HealthReporter> _logger;

    public HealthReporter(
        ConfigService config,
        SqliteBuffer buffer,
        IdentityStore identity,
        CollectorOptions options,
        MinuteCounter minute,
        LinkGate linkGate,
        Forwarder forwarder,
        IHttpClientFactory httpFactory,
        ILogger<HealthReporter> logger)
    {
        _config = config;
        _buffer = buffer;
        _identity = identity;
        _options = options;
        _minute = minute;
        _linkGate = linkGate;
        _forwarder = forwarder;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _config.EnrolledTask.WaitAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cfg = _config.CurrentConfig;
            var intervalSecs = cfg?.HealthReportSeconds ?? 30;
            await Task.Delay(TimeSpan.FromSeconds(intervalSecs), stoppingToken);

            if (cfg is null || !_linkGate.IsUp) continue;

            try
            {
                await ReportAsync(cfg, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health report failed");
            }
        }
    }

    private async Task ReportAsync(CollectorConfig cfg, CancellationToken ct)
    {
        var bufferDepth = await _buffer.CountAsync(ct);
        var oldestEvent = await _buffer.GetOldestEventTimeAsync(ct);
        var minuteCounts = await _buffer.GetUnacknowledgedMinuteCountsAsync(10_080, ct);
        var sources = _config.CurrentSources;

        long diskFreeBytes = 0;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(_options.DataDir));
            if (!string.IsNullOrEmpty(root))
                diskFreeBytes = new DriveInfo(root).AvailableFreeSpace;
        }
        catch { /* best-effort */ }

        var body = BuildJson(cfg, bufferDepth, oldestEvent, diskFreeBytes, sources, minuteCounts);

        var client = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{cfg.ManagementUrl.TrimEnd('/')}/v1/health")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        using var response = await client.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
            await _buffer.AcknowledgeMinuteCountsAsync(minuteCounts.Select(m => m.MinuteStart), ct);
        else
            _logger.LogWarning("Health report returned {Status}", response.StatusCode);
    }

    private string BuildJson(
        CollectorConfig cfg,
        int bufferDepth,
        DateTimeOffset? oldestEvent,
        long diskFreeBytes,
        IReadOnlyList<SimulatedSource> sources,
        List<MinuteCount> minuteCounts)
    {
        var buf = new ArrayBufferWriter<byte>();
        using var w = new Utf8JsonWriter(buf);
        w.WriteStartObject();
        w.WriteString("collectorId", _identity.CollectorId);
        w.WriteString("reportedAt", Ts(DateTimeOffset.UtcNow));
        w.WriteString("softwareVersion", _options.CollectorSoftwareVersion);
        w.WriteNumber("configVersion", cfg.ConfigVersion);
        w.WriteString("configHash", _config.GetConfigHash());
        w.WriteNumber("bufferDepthEvents", bufferDepth);
        w.WriteNumber("bufferCapacityEvents", cfg.CapacityEvents);
        if (oldestEvent.HasValue)
            w.WriteString("oldestBufferedEventTime", Ts(oldestEvent.Value));
        else
            w.WriteNull("oldestBufferedEventTime");
        w.WriteNumber("overflowDroppedTotal", _buffer.OverflowDroppedTotal);
        w.WriteNumber("rejectedBatchesTotal", _forwarder.RejectedBatchesTotal);
        w.WriteNumber("invalidAtEdgeTotal", _minute.InvalidAtEdgeTotal);
        w.WriteNumber("clockOffsetMs", 0);
        w.WriteNumber("diskFreeBytes", diskFreeBytes);

        w.WritePropertyName("sources");
        w.WriteStartArray();
        foreach (var s in sources)
        {
            w.WriteStartObject();
            w.WriteString("sourceId", s.SourceId);
            if (s.LastReadAt != DateTimeOffset.MinValue)
                w.WriteString("lastReadAt", Ts(s.LastReadAt));
            else
                w.WriteNull("lastReadAt");
            w.WriteNumber("lastSequence", s.LastSequence);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WritePropertyName("minuteCounts");
        w.WriteStartArray();
        foreach (var mc in minuteCounts)
        {
            w.WriteStartObject();
            w.WriteString("minuteStart", mc.MinuteStart);
            w.WriteNumber("produced", mc.Produced);
            w.WriteNumber("droppedAtEdge", mc.DroppedAtEdge);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteEndObject();
        w.Flush();
        return Encoding.UTF8.GetString(buf.WrittenSpan);
    }

    private static string Ts(DateTimeOffset dt) =>
        dt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
}
