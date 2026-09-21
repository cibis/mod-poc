using System.Security.Cryptography.X509Certificates;
using Mod.Platform.Common.Registry;

namespace Mod.Platform.Common.Certificates;

// PoC simplification: validates that the client cert is signed by the collector CA,
// then looks up the collector by the ID carried in the X-Collector-Id request header.
// One shared cert is used for all collectors; no per-collector cert issuance or renewal.
//
// Production would: extract collectorId from cert CN, verify matching SAN URI, look up
// cert thumbprint in registry.Certificate (revocation check), and never trust a header.
public sealed class CertificateValidator(X509Certificate2 caCertificate, RegistryReader registry)
{
    public async Task<CertValidationResult> ValidateAsync(
        X509Certificate2 cert,
        string collectorIdHint,
        CancellationToken ct = default)
    {
        // Structural check: cert must be signed by our collector CA.
        if (!ValidateChain(cert))
            return new(null, CertValidationError.InvalidCertificate);

        var now = DateTime.UtcNow;
        if (now < cert.NotBefore || now > cert.NotAfter)
            return new(null, CertValidationError.InvalidCertificate);

        // PoC: collectorId comes from the X-Collector-Id header, not the cert CN.
        if (!Guid.TryParse(collectorIdHint, out var collectorId))
            return new(null, CertValidationError.InvalidCertificate);

        // Verify the collector is registered and active.
        var collectorRecord = await registry.GetCollectorByIdAsync(collectorId, ct);
        if (collectorRecord is null || collectorRecord.Status is not ("Registered" or "Enrolled"))
            return new(null, CertValidationError.ForbiddenAccess);

        var thumbprint = ClientCertificateParser.ComputeThumbprint(cert);
        return new(
            new CollectorIdentity(collectorId, collectorRecord.TenantId, collectorRecord.SiteId, thumbprint),
            null);
    }

    private bool ValidateChain(X509Certificate2 cert)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(cert);
    }
}
