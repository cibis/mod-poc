namespace Mod.ReportingApi.Data.Entities;

public class RollupMinute
{
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid LineId { get; set; }
    public Guid AssetId { get; set; }
    public DateTime MinuteStart { get; set; }
    public int EventCount { get; set; }
    public long CounterDelta { get; set; }
    public int FaultEventCount { get; set; }
    public string? LastState { get; set; }
    public double? ProcessValueAvg { get; set; }
    public double? ProcessValueMin { get; set; }
    public double? ProcessValueMax { get; set; }
    public bool IsRestated { get; set; }
    public int RestatementCount { get; set; }
    public DateTime FirstComputedAt { get; set; }
    public DateTime LastComputedAt { get; set; }
}
