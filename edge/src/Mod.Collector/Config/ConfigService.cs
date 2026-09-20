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
    private readonly EnrolmentClient _enrolment;
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
    private string? _configJson;       // raw JSON of the currently applied config
    private string? _configEtag;       // last ETag from GET /v1/config

    // Mutable source list — replaced atomically on config changes.
    private volatile IReadOnlyList<SimulatedSource> _sources = Array.Empty<SimulatedSource>();
    private readonly List<(SimulatedSource source, CancellationTokenSource cts)> _activeSourceEntries = new();
    private readonly SemaphoreSlim _sourceLock = new(1, 1);

    // ConfigService signals this when enrolled so other services can wait.
    private readonly TaskCompletionSource _enrolledTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task EnrolledTask => _enrolledTcs.Task;

    public CollectorConfig? CurrentConfig => _currentConfig;
    public IReadOnlyList<SimulatedSource> CurrentSources => _sources;

    // Called by SimChannelListener on SetRate/SetPaused commands.
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

    // Called by ProductCommandListener to force an immediate config refresh.
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
        EnrolmentClient enrolment,
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
        _enrolment = enrolment;
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
        _identity.Initialize();

        string initialConfigJson;

        if (_identity.HasValidCertificate)
        {
            // Restart path: load the last-persisted config, then continue polling.
            initialConfigJson = TryLoadPersistedConfig()
                ?? await EnrolWithRetryAsync(stoppingToken);
        }
        else
        {
            initialConfigJson = await EnrolWithRetryAsync(stoppingToken);
        }

        await TryApplyConfigJsonAsync(initialConfigJson, stoppingToken);
        _enrolledTcs.TrySetResult();

        // Config poll + certificate renewal loop.
        while (!stoppingToken.IsCancellationRequested)
        {
            var config = _currentConfig!;
            await Task.Delay(TimeSpan.FromSeconds(config.ConfigPollSeconds), stoppingToken);

            // Certificate renewal check.
            if (_identity.NeedsRenewal)
                await _enrolment.TryRenewAsync(_currentConfig!.ManagementUrl, stoppingToken);

            // Config poll.
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

    private async Task<string> EnrolWithRetryAsync(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                var configJson = await _enrolment.EnrolAsync(ct);
                PersistConfig(configJson);
                return configJson;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Enrolment failed — retrying in 30 s");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
    }

    // Returns the new config JSON if the server returned 200, null if 304 (unchanged).
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

        // Build new sources.
        var newSources = newConfig.Sources.Select(src =>
            new SimulatedSource(src, _buffer, _injector, _validator, _minute,
                _loggerFactory.CreateLogger<SimulatedSource>(),
                _simState.EventsPerSecondPerSource)).ToList();

        // Try starting sources; on failure keep previous config.
        await _sourceLock.WaitAsync(ct);
        try
        {
            // Start new sources with a linked CTS.
            var newEntries = new List<(SimulatedSource, CancellationTokenSource)>();
            foreach (var src in newSources)
            {
                var cts = new CancellationTokenSource();
                await src.StartAsync(cts.Token);
                newEntries.Add((src, cts));
            }

            // Apply sim state (in case sources were recreated mid-session).
            if (_simState.Paused)
                foreach (var s in newSources) s.SetPaused(true);

            // Stop old sources.
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

        // Update buffer capacity if it changed.
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

    // Returns the SHA-256 hex of the current config JSON (UTF-8 bytes).
    public string GetConfigHash()
    {
        if (_configJson is null) return string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(_configJson));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // Parses the management-API ConfigDocument into CollectorConfig.
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
                // processValue may be an object or absent
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

        // Must have collectorId so that a valid enrolled state is required before apply.
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
            Sources = sources,
            MaxEvents = GetNestedInt("batching", "maxEvents", 500),
            MaxBytes = GetNestedInt("batching", "maxBytes", 262_144),
            FlushIntervalMs = GetNestedInt("batching", "flushIntervalMs", 2_000),
            MaxBatchesPerSecond = GetNestedDouble("forwarder", "maxBatchesPerSecond", 5.0),
            RetryBaseMs = GetNestedInt("forwarder", "retryBaseMs", 1_000),
            RetryMaxMs = GetNestedInt("forwarder", "retryMaxMs", 60_000),
            CapacityEvents = GetNestedInt("buffer", "capacityEvents", 200_000),
            ConfigPollSeconds = GetNestedInt("intervals", "configPollSeconds", 30),
            HealthReportSeconds = GetNestedInt("intervals", "healthReportSeconds", 30),
            CommandChannelEnabled = r.TryGetProperty("commandChannelEnabled", out var cce) && cce.GetBoolean(),
        };
    }
}
