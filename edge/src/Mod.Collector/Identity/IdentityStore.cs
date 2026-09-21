// PoC: simulated enrollment — one shared TLS client cert for all collectors, no renewal.
// Each collector loads a pre-provisioned PKCS#12 cert from the COLLECTOR_CLIENT_CERT_B64
// env var and populates CollectorId/TenantId/SiteId from COLLECTOR_ID/TENANT_ID/SITE_ID.
// No key generation, no CSR, no HTTP call to /v1/enrol, no cert renewal timer.
//
// Production design: each collector generates its own EC P-256 key pair, produces a CSR,
// and calls POST /v1/enrol with the single-use ENROLMENT_TOKEN to receive a per-collector
// certificate signed by the CA. Before expiry (e.g. < 48 h remaining) the collector calls
// POST /v1/renew with the existing mTLS cert to receive a fresh cert. IdentityStore would
// store the key and cert in the DATA_DIR volume so they survive container restarts.
using System.Security.Cryptography.X509Certificates;

namespace Mod.Collector.Identity;

internal sealed class IdentityStore
{
    private X509Certificate2? _cert;
    private readonly X509Certificate2Collection _tlsCertCollection = new();
    private volatile bool _authRejected;

    public string CollectorId { get; private set; } = string.Empty;
    public string TenantId { get; private set; } = string.Empty;
    public string SiteId { get; private set; } = string.Empty;

    public bool IsEnrolled => _cert is not null;

    public bool AuthRejected
    {
        get => _authRejected;
        set => _authRejected = value;
    }

    // Populated after Initialize(); handed to SocketsHttpHandler.SslOptions.ClientCertificates
    // so the Linux/OpenSSL PAL properly calls SSL_use_certificate for mTLS.
    public X509Certificate2Collection TlsCertCollection => _tlsCertCollection;

    // Must be called once at startup before any other member is used.
    public void Initialize(CollectorOptions options)
    {
        var pkcs12 = Convert.FromBase64String(options.ClientCertB64);
        _cert = X509CertificateLoader.LoadPkcs12(
            pkcs12, string.Empty,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);

        _tlsCertCollection.Clear();
        _tlsCertCollection.Add(_cert);

        CollectorId = options.CollectorId;
        TenantId = options.TenantId;
        SiteId = options.SiteId;
    }
}
