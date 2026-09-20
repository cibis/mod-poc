namespace Mod.ReportingApi.Data.Entities;

public class Site
{
    public Guid SiteId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string RegionLabel { get; set; } = "";
    public string TimeZone { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public ICollection<Line> Lines { get; set; } = [];
}
