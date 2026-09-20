namespace Mod.ReportingApi.Auth;

public interface ITenantContext
{
    Guid TenantId { get; }
}
