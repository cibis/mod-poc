namespace Mod.ReportingApi.Data.Entities;

public class AppUser
{
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Kind { get; set; } = "";
    public Guid? TenantId { get; set; }
    public string DisplayName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
