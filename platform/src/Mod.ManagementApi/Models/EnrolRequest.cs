namespace Mod.ManagementApi.Models;

public sealed class EnrolRequest
{
    public string EnrolmentToken { get; init; } = "";
    public string CsrPem { get; init; } = "";
    public string SoftwareVersion { get; init; } = "";
}
