namespace Mod.ManagementApi.ServiceBus;

public sealed class SasTokenFactory(SasTokenFactory.Options opts)
{
    public sealed record Options(
        string NamespaceFqdn,
        string KeyName,
        string Key,
        int LifetimeMinutes);

    public sealed record TokenResult(
        string NamespaceFqdn,
        string RequestQueue,
        string ReplyQueue,
        string KeyName,
        string Key,
        DateTimeOffset ExpiresAt);

    public TokenResult MintCollectorTokens(Guid tenantId, Guid collectorId) => new(
        opts.NamespaceFqdn,
        $"cmd~{tenantId:D}~{collectorId:D}",
        $"reply~{tenantId:D}~{collectorId:D}",
        opts.KeyName,
        opts.Key,
        DateTimeOffset.UtcNow.AddMinutes(opts.LifetimeMinutes));
}
