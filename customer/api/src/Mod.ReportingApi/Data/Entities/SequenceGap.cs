namespace Mod.ReportingApi.Data.Entities;

public class SequenceGap
{
    public long GapId { get; set; }
    public Guid TenantId { get; set; }
    public Guid CollectorId { get; set; }
    public string SourceId { get; set; } = "";
    public long FromSequence { get; set; }
    public long ToSequence { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? FilledAt { get; set; }
}
