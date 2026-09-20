namespace Mod.ReportingApi.Data.Entities;

public class CollectorFreshness
{
    public Guid CollectorId { get; set; }
    public Guid TenantId { get; set; }
    public Guid SiteId { get; set; }
    public DateTime LastEventTime { get; set; }
    public DateTime LastReceivedAt { get; set; }
    public DateTime LastProcessedAt { get; set; }
    public long EventsProcessedTotal { get; set; }
    public long EventsDeadLetteredTotal { get; set; }
}
