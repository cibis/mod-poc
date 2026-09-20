namespace Mod.ReportingApi.Data.Entities;

public class Asset
{
    public Guid AssetId { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public Guid LineId { get; set; }
    public string Name { get; set; } = "";
    public string AssetType { get; set; } = "";
    public string CounterName { get; set; } = "";
    public string? ProcessValueName { get; set; }
    public string? ProcessValueUnit { get; set; }
    public double? ProcessValueMin { get; set; }
    public double? ProcessValueMax { get; set; }
    public DateTime CreatedAt { get; set; }
}
