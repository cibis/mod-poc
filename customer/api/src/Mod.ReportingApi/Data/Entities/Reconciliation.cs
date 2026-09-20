namespace Mod.ReportingApi.Data.Entities;

public class Reconciliation
{
    public Guid CollectorId { get; set; }
    public DateTime MinuteStart { get; set; }
    public Guid TenantId { get; set; }
    public int? ProducedCount { get; set; }
    public int? DroppedAtEdgeCount { get; set; }
    public int AcceptedCount { get; set; }
    public int DeadLetteredCount { get; set; }
}
