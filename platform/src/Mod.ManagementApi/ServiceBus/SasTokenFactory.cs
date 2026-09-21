using System.Security.Cryptography;
using System.Text;

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
        string RequestSasToken,
        string ReplySasToken,
        DateTimeOffset ExpiresAt);

    public TokenResult MintCollectorTokens(Guid tenantId, Guid collectorId)
    {
        var requestQueue = $"cmd~{tenantId:D}~{collectorId:D}";
        var replyQueue = $"reply~{tenantId:D}~{collectorId:D}";
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(opts.LifetimeMinutes);

        // PoC: namespace-scoped SAS so ServiceBusClient(fqdn, AzureSasCredential) works for any queue.
        // Production: issue per-entity tokens scoped to the individual queue resource URI.
        var namespaceSas = Mint($"https://{opts.NamespaceFqdn}/", expiresAt);
        return new TokenResult(
            opts.NamespaceFqdn,
            requestQueue,
            replyQueue,
            namespaceSas,
            namespaceSas,
            expiresAt);
    }

    private string Mint(string resourceUri, DateTimeOffset expiresAt)
    {
        var expiry = expiresAt.ToUnixTimeSeconds().ToString();
        var encodedUri = Uri.EscapeDataString(resourceUri);
        var strToSign = encodedUri + "\n" + expiry;

        var keyBytes = Convert.FromBase64String(opts.Key);
        using var hmac = new HMACSHA256(keyBytes);
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(strToSign)));

        return $"SharedAccessSignature sr={encodedUri}&sig={Uri.EscapeDataString(sig)}&se={expiry}&skn={opts.KeyName}";
    }
}
