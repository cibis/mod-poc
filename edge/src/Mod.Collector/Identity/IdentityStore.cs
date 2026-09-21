using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Mod.Collector.Identity;

internal sealed class IdentityStore
{
    private readonly string _keyPath;
    private readonly string _certPath;
    private readonly string _caPath;
    private readonly string _metaPath;
    private readonly ILogger<IdentityStore> _logger;

    private ECDsa? _key;
    private string? _certPem;
    private string? _caCertPem;
    private IdentityMeta? _meta;
    private volatile bool _authRejected;

    public IdentityStore(CollectorOptions options, ILogger<IdentityStore> logger)
    {
        var dir = Path.Combine(options.DataDir, "identity");
        Directory.CreateDirectory(dir);
        _keyPath = Path.Combine(dir, "key.pem");
        _certPath = Path.Combine(dir, "cert.pem");
        _caPath = Path.Combine(dir, "ca.pem");
        _metaPath = Path.Combine(dir, "meta.json");
        _logger = logger;
    }

    // Must be called once at startup before any other member is used.
    public void Initialize()
    {
        LoadOrCreateKey();
        TryLoadIdentity();
    }

    public bool IsEnrolled => _meta is not null && _certPem is not null;

    public bool HasValidCertificate =>
        IsEnrolled && DateTimeOffset.UtcNow < _meta!.CertExpiresAt;

    public bool NeedsRenewal =>
        IsEnrolled && (_meta!.CertExpiresAt - DateTimeOffset.UtcNow) < TimeSpan.FromHours(48);

    public string CollectorId => _meta?.CollectorId ?? throw new InvalidOperationException("Not enrolled");
    public string TenantId => _meta?.TenantId ?? throw new InvalidOperationException("Not enrolled");
    public string SiteId => _meta?.SiteId ?? throw new InvalidOperationException("Not enrolled");
    public DateTimeOffset CertExpiresAt => _meta?.CertExpiresAt ?? DateTimeOffset.MinValue;

    public bool AuthRejected
    {
        get => _authRejected;
        set => _authRejected = value;
    }

    public string GetCsrPem()
    {
        if (_key is null) throw new InvalidOperationException("Key not initialised");
        var req = new CertificateRequest("CN=pending", _key, HashAlgorithmName.SHA256);
        return req.CreateSigningRequestPem();
    }

    public void SaveIdentity(string collectorId, string tenantId, string siteId,
        string certPem, string caCertPem, DateTimeOffset expiresAt)
    {
        _certPem = certPem;
        _caCertPem = caCertPem;
        _meta = new IdentityMeta(collectorId, tenantId, siteId, expiresAt);
        File.WriteAllText(_certPath, certPem);
        File.WriteAllText(_caPath, caCertPem);
        File.WriteAllText(_metaPath, JsonSerializer.Serialize(_meta));
        _logger.LogInformation("Identity saved, collectorId={CollectorId}", collectorId);
    }

    public void UpdateCertificate(string certPem, DateTimeOffset expiresAt)
    {
        if (_meta is null) throw new InvalidOperationException("Not enrolled");
        _certPem = certPem;
        _meta = _meta with { CertExpiresAt = expiresAt };
        File.WriteAllText(_certPath, certPem);
        File.WriteAllText(_metaPath, JsonSerializer.Serialize(_meta));
    }

    // Returns a new X509Certificate2 instance with the private key attached — for TLS client auth.
    // Uses CreateFromPem(cert, key) rather than CopyWithPrivateKey so the ephemeral key is
    // accessible to the TLS stack on Linux/OpenSSL.
    public X509Certificate2 GetCertificateWithKey()
    {
        if (_certPem is null || _key is null) throw new InvalidOperationException("No certificate");
        return X509Certificate2.CreateFromPem(_certPem, _key.ExportPkcs8PrivateKeyPem());
    }

    // --- private helpers ---

    private void LoadOrCreateKey()
    {
        if (File.Exists(_keyPath))
        {
            try
            {
                var pem = File.ReadAllText(_keyPath);
                var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                _key = ecdsa;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load existing key — generating a new one");
            }
        }
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(_keyPath, _key.ExportPkcs8PrivateKeyPem());
        _logger.LogInformation("Generated new EC P-256 key pair");
    }

    private void TryLoadIdentity()
    {
        if (!File.Exists(_certPath) || !File.Exists(_caPath) || !File.Exists(_metaPath))
            return;
        try
        {
            _certPem = File.ReadAllText(_certPath);
            _caCertPem = File.ReadAllText(_caPath);
            _meta = JsonSerializer.Deserialize<IdentityMeta>(File.ReadAllText(_metaPath))!;
            _logger.LogInformation("Loaded existing identity, collectorId={CollectorId}", _meta.CollectorId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load existing certificate — will re-enrol");
            _certPem = null;
            _caCertPem = null;
            _meta = null;
        }
    }
}

internal sealed record IdentityMeta(
    string CollectorId,
    string TenantId,
    string SiteId,
    DateTimeOffset CertExpiresAt);
