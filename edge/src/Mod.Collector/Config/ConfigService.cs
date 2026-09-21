using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mod.Collector.Buffer;
using Mod.Collector.Edge;
using Mod.Collector.Health;
using Mod.Collector.Identity;
using Mod.Collector.Models;
using Mod.Collector.Simulation;
using Mod.Collector.Sources;

namespace Mod.Collector.Config;

internal sealed class ConfigService : BackgroundService
{
    private readonly IdentityStore _identity;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SqliteBuffer _buffer;
    private readonly InvalidDataInjector _injector;
    private readonly EdgeValidator _validator;
    private readonly MinuteCounter _minute;
    private readonly SimState _simState;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CollectorOptions _options;
    private readonly ILogger<ConfigService> _logger;

    private volatile CollectorConfig? _currentConfig;
    private string? _persistedConfigPath;
    private string? _configJson;
    private string? _configEtag;

    private volatile IReadOnlyList<SimulatedSource> _sources = Array.Empty<SimulatedSource>();
    private readonly List<(SimulatedSource source, CancellationTokenSource cts)> _activeSourceEntries = new();
    private readonly SemaphoreSlim _sourceLock = new(1, 1);

    // ConfigService signals this when the initial config is loaded so other services can start.
    private readonly TaskCompletionSource _enrolledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task EnrolledTask => _enrolledTcs.Task;

    public CollectorConfig? CurrentConfig => _currentConfig;
    public IReadOnlyList<SimulatedSource> CurrentSources => _sources;

    public void SetRateAll(int eventsPerSecond)
    {
        foreach (var s in _sources)
            s.SetRate(eventsPerSecond);
    }

    public void SetPausedAll(bool paused)
    {
        foreach (var s in _sources)
            s.SetPaused(paused);
    }

    public async Task<int> ForceConfigRefreshAsync(CancellationToken ct)
    {
        var config = _currentConfig;
        if (config is null) return 0;
        var newConfigJson = await PollConfigAsync(config.ManagementUrl, null, ct);
        if (newConfigJson is not null)
            await TryApplyConfigJsonAsync(newConfigJson, ct);
        return _currentConfig?.ConfigVersion ?? 0;
    }

    public ConfigService(
        IdentityStore identity,
        IHttpClientFactory httpFactory,
        SqliteBuffer buffer,
        InvalidDataInjector injector,
        EdgeValidator validator,
        MinuteCounter minute,
        SimState simState,
        ILoggerFactory loggerFactory,
        CollectorOptions options,
        ILogger<ConfigService> logger)
    {
        _identity = identity;
        _httpFactory = httpFactory;
        _buffer = buffer;
        _injector = injector;
        _validator = validator;
        _minute = minute;
        _simState = simState;
        _loggerFactory = loggerFactory;
        _options = options;
        _logger = logger;
        _persistedConfigPath = Path.Combine(options.DataDir, "identity", "config.json");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PoC: load shared cert and identity from env vars — no HTTP enrollment call.
        // Production: call /v1/enrol with the single-use ENROLMENT_TOKEN to get a
        // per-collector cert, then store it in the DATA_DIR volume.
        _identity.Initialize(_options);

        // Load config from management API (retry indefinitely until cancellation).
        var initialConfigJson = TryLoadPersistedConfig()
            ?? await FetchConfigWithRetryAsync(_options.ManagementUrl, stoppingToken);

        await TryApplyConfigJsonAsync(initialConfigJson, stoppingToken);
        _enrolledTcs.TrySetResult();

        while (!stoppingToken.IsCancellationRequested)
        {
            var config = _currentConfig!;
            await Task.Delay(TimeSpan.FromSeconds(config.ConfigPollSeconds), stoppingToken);

            try
            {
                var newJson = await PollConfigAsync(config.ManagementUrl, _configEtag, stoppingToken);
                if (newJson is not null)
                    await TryApplyConfigJsonAsync(newJson, stoppingToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Config poll failed — keeping last-known-good");
            }
        }

        await StopAllSourcesAsync(CancellationToken.None);
    }

    // --- private helpers ---

    private async Task<string> FetchConfigWithRetryAsync(string managementUrl, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                var json = await PollConfigAsync(managementUrl, null, ct);
                if (json is not null)
                {
                    PersistConfig(json);
                    return json;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Config fetch failed — retrying in 30 s");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task<string?> PollConfigAsync(string managementUrl, string? etag, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{managementUrl.TrimEnd('/')}/v1/config");

        if (!string.IsNullOrEmpty(etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        using var response = await client.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            return null;

        response.EnsureSuccessStatusCode();

        if (response.Headers.ETag is { } tag)
            _configEtag = tag.ToString();

        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task TryApplyConfigJsonAsync(string json, CancellationToken ct)
    {
        CollectorConfig newConfig;
        try
        {
            newConfig = ParseConfig(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse config — keeping last-known-good");
            return;
        }

        var newSources = newConfig.Sources.Select(src =>
            new SimulatedSource(src, _buffer, _injector, _validator, _minute,
                _loggerFactory.CreateLogger<SimulatedSource>(),
                _simState.EventsPerSecondPerSource)).ToList();

        await _sourceLock.WaitAsync(ct);
        try
        {
            var newEntries = new List<(SimulatedSource, CancellationTokenSource)>();
            foreach (var src in newSources)
            {
                var cts = new CancellationTokenSource();
                await src.StartAsync(cts.Token);
                newEntries.Add((src, cts));
            }

            if (_simState.Paused)
                foreach (var s in newSources) s.SetPaused(true);

            foreach (var (old, oldCts) in _activeSourceEntries)
            {
                await old.StopAsync(CancellationToken.None);
                oldCts.Dispose();
            }
            _activeSourceEntries.Clear();
            _activeSourceEntries.AddRange(newEntries);

            _sources = newSources.AsReadOnly();
        }
        finally
        {
            _sourceLock.Release();
        }

        _currentConfig = newConfig;
        _configJson = json;

        _buffer.SetCapacity(newConfig.CapacityEvents);

        PersistConfig(json);
        _logger.LogInformation("Config v{Version} applied ({Sources} sources)",
            newConfig.ConfigVersion, newConfig.Sources.Length);
    }

    private async Task StopAllSourcesAsync(CancellationToken ct)
    {
        await _sourceLock.WaitAsync(ct);
        try
        {
            foreach (var (src, cts) in _activeSourceEntries)
            {
                await src.StopAsync(CancellationToken.None);
                cts.Dispose();
            }
            _activeSourceEntries.Clear();
            _sources = Array.Empty<SimulatedSource>();
        }
        finally
        {
            _sourceLock.Release();
        }
    }

    private string? TryLoadPersistedConfig()
    {
        try
        {
            if (File.Exists(_persistedConfigPath))
                return File.ReadAllText(_persistedConfigPath!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read persisted config");
        }
        return null;
    }

    private void PersistConfig(string json)
    {
        try
        {
            File.WriteAllText(_persistedConfigPath!, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist config");
        }
    }

    public string GetConfigHash()
    {
        if (_configJson is null) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(_configJson));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static CollectorConfig ParseConfig(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        int GetInt(string name, int def) =>
            r.TryGetProperty(name, out var v) ? v.GetInt32() : def;

        int GetNestedInt(string obj, string name, int def) =>
            r.TryGetProperty(obj, out var o) && o.TryGetProperty(name, out var v)
                ? v.GetInt32() : def;

        double GetNestedDouble(string obj, string name, double def) =>
            r.TryGetProperty(obj, out var o) && o.TryGetProperty(name, out var v)
                ? v.GetDouble() : def;

        var sources = Array.Empty<SourceConfig>();
        if (r.TryGetProperty("sources", out var srcsEl) && srcsEl.ValueKind == JsonValueKind.Array)
        {
            sources = srcsEl.EnumerateArray().Select(s =>
            {
                var pvList = new List<ProcessValueSpec>();
                if (s.TryGetProperty("processValue", out var pv) && pv.ValueKind == JsonValueKind.Object)
                {
                    pvList.Add(new ProcessValueSpec(
                        pv.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        pv.TryGetProperty("min", out var mn) ? mn.GetDouble() : 0,
                        pv.TryGetProperty("max", out var mx) ? mx.GetDouble() : 100,
                        pv.TryGetProperty("unit", out var u) ? u.GetString() ?? "" : ""));
                }
                return new SourceConfig
                {
                    SourceId = s.TryGetProperty("sourceId", out var sid) ? sid.GetString() ?? "" : "",
                    AssetName = s.TryGetProperty("assetName", out var an) ? an.GetString() ?? "" : "",
                    AssetType = s.TryGetProperty("assetType", out var at) ? at.GetString() ?? "" : "",
                    CounterName = s.TryGetProperty("counterName", out var cn) ? cn.GetString() ?? "parts" : "parts",
                    ProcessValues = pvList.ToArray(),
                };
            }).ToArray();
        }

        var collectorId = r.TryGetProperty("collectorId", out var cid) ? cid.GetString()! : "";
        var tenantId = r.TryGetProperty("tenantId", out var tid) ? tid.GetString()! : "";
        var siteId = r.TryGetProperty("siteId", out var siid) ? siid.GetString()! : "";

        return new CollectorConfig
        {
            ConfigVersion = GetInt("configVersion", 0),
            CollectorId = collectorId,
            TenantId = tenantId,
            SiteId = siteId,
            IngestUrl = r.TryGetProperty("ingestUrl", out var iu) ? iu.GetString()! : "",
            ManagementUrl = r.TryGetProperty("managementUrl", out var mu) ? mu.GetString()! : "",
            ConfigPollSeconds = GetInt("configPollSeconds", 30),
            HealthReportSeconds = GetInt("healthReportSeconds", 30),
            CapacityEvents = GetInt("capacityEvents", 200_000),
            MaxEvents = GetNestedInt("forwarding", "batchSize", 500),
            MaxBytes = GetNestedInt("forwarding", "maxBytes", 262_144),
            MaxBatchesPerSecond = GetNestedDouble("forwarding", "maxBatchesPerSecond", 5.0),
            CommandChannelEnabled = r.TryGetProperty("commandChannelEnabled", out var cc) && cc.GetBoolean(),
            Sources = sources,
        };
    }
}
