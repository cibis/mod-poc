using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Security.KeyVault.Secrets;

namespace Mod.ManagementApi.Pki;

public sealed class CertificateAuthority : BackgroundService
{
    public sealed record Options(
        string CertName,
        string? PfxPath,
        int ValidityDays);

    private readonly Options _options;
    private readonly SecretClient? _secretClient;
    private volatile X509Certificate2? _caCert;

    public CertificateAuthority(Options options, SecretClient? secretClient = null)
    {
        _options = options;
        _secretClient = secretClient;
    }

    public string CaCertificatePem =>
        _caCert?.ExportCertificatePem()
        ?? throw new InvalidOperationException("CA certificate not loaded");

    // Returns (signedCert, errorCode). errorCode non-null means CsrInvalid.
    public (X509Certificate2? Cert, string? ErrorCode) IssueCertificate(string csrPem, Guid collectorId)
    {
        var ca = _caCert ?? throw new InvalidOperationException("CA certificate not loaded");

        CertificateRequest loaded;
        try
        {
            loaded = CertificateRequest.LoadSigningRequestPem(csrPem.AsSpan(), HashAlgorithmName.SHA256);
        }
        catch
        {
            return (null, "CsrInvalid");
        }

        using var ecKey = loaded.PublicKey.GetECDsaPublicKey();
        if (ecKey is null || ecKey.KeySize != 256)
            return (null, "CsrInvalid");

        var req = new CertificateRequest(
            new X500DistinguishedName($"CN={collectorId:D}"),
            loaded.PublicKey,
            HashAlgorithmName.SHA256);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri($"urn:mod:collector:{collectorId:D}"));
        req.CertificateExtensions.Add(sanBuilder.Build());
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, critical: false));
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, critical: true));

        var serial = new byte[16];
        RandomNumberGenerator.Fill(serial);
        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var notAfter = notBefore.AddDays(_options.ValidityDays).AddMinutes(5);

        var issued = req.Create(ca, notBefore, notAfter, serial);
        return (issued, null);
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await LoadAsync(stoppingToken);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        byte[] pkcs12;
        if (_options.PfxPath is not null)
        {
            pkcs12 = await File.ReadAllBytesAsync(_options.PfxPath, ct);
        }
        else
        {
            var secret = await _secretClient!.GetSecretAsync(_options.CertName, cancellationToken: ct);
            pkcs12 = Convert.FromBase64String(secret.Value.Value);
        }

        _caCert = X509CertificateLoader.LoadPkcs12(
            pkcs12,
            string.Empty,
            X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }
}
