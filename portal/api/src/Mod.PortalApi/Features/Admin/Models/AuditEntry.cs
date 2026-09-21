namespace Mod.PortalApi.Features.Admin.Models;

internal sealed class AuditEntry
{
    public long AuditId { get; set; }
    public DateTime At { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorKind { get; set; } = "";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string? TargetId { get; set; }
    public Guid? TenantId { get; set; }
    public string? DetailsJson { get; set; }
}
