namespace Mod.ReportingApi.Data.Entities;

public class Line
{
    public Guid LineId { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public ICollection<Asset> Assets { get; set; } = [];
}
