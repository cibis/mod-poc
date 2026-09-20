namespace Mod.ReportingApi.Auth;

public sealed class TenantContext : ITenantContext
{
    public Guid TenantId { get; set; }
}
