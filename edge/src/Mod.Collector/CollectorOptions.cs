namespace Mod.Collector;

internal sealed class CollectorOptions
{
    public required string CollectorId { get; init; }
    public required string ManagementUrl { get; init; }
    public string? EnrolmentToken { get; init; }
    public required string DataDir { get; init; }
    public required string SimSbFqdn { get; init; }
    public required string SimCommandQueue { get; init; }
    public required string SimCommandSas { get; init; }
    public required string SimStatusQueue { get; init; }
    public required string SimStatusSas { get; init; }
    public int SimInitialRate { get; init; }
    public required string CollectorSoftwareVersion { get; init; }
    public LogLevel ParsedLogLevel { get; init; }
    public bool LocalInsecureTls { get; init; }

    public static CollectorOptions Load()
    {
        var missing = new List<string>();

        string Require(string name)
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(v)) missing.Add(name);
            return v ?? string.Empty;
        }

        var collectorId = Require("COLLECTOR_ID");
        var managementUrl = Require("MANAGEMENT_URL");
        var enrolmentToken = Environment.GetEnvironmentVariable("ENROLMENT_TOKEN");
        var dataDir = Environment.GetEnvironmentVariable("DATA_DIR") is { Length: > 0 } d ? d : "/data";
        var simSbFqdn = Require("SIM_SB_FQDN");
        var simCommandQueue = Require("SIM_COMMAND_QUEUE");
        var simCommandSas = Require("SIM_COMMAND_SAS");
        var simStatusQueue = Require("SIM_STATUS_QUEUE");
        var simStatusSas = Require("SIM_STATUS_SAS");
        var simInitialRate = int.TryParse(Environment.GetEnvironmentVariable("SIM_INITIAL_RATE"), out var r) ? r : 1;
        var collectorSoftwareVersion = Require("COLLECTOR_SOFTWARE_VERSION");
        var logLevelStr = Environment.GetEnvironmentVariable("LOG_LEVEL") is { Length: > 0 } ll ? ll : "Information";
        var localInsecureTls = "true".Equals(Environment.GetEnvironmentVariable("LOCAL_INSECURE_TLS"), StringComparison.OrdinalIgnoreCase);

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Required environment variables not set: {string.Join(", ", missing)}");

        var logLevel = Enum.TryParse<LogLevel>(logLevelStr, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;

        return new CollectorOptions
        {
            CollectorId = collectorId,
            ManagementUrl = managementUrl,
            EnrolmentToken = enrolmentToken,
            DataDir = dataDir,
            SimSbFqdn = simSbFqdn,
            SimCommandQueue = simCommandQueue,
            SimCommandSas = simCommandSas,
            SimStatusQueue = simStatusQueue,
            SimStatusSas = simStatusSas,
            SimInitialRate = simInitialRate,
            CollectorSoftwareVersion = collectorSoftwareVersion,
            ParsedLogLevel = logLevel,
            LocalInsecureTls = localInsecureTls,
        };
    }
}
