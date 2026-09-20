using System.Formats.Asn1;
using System.Security.Cryptography.X509Certificates;
using Mod.Platform.Common.Registry;

namespace Mod.Platform.Common.Certificates;

public sealed class CertificateValidator(X509Certificate2 caCertificate, RegistryReader registry)
{
    public async Task<CertValidationResult> ValidateAsync(
        X509Certificate2 cert,
        CancellationToken ct = default)
    {
        // Structural failures → 401
        if (!ValidateChain(cert))
            return new(null, CertValidationError.InvalidCertificate);

        var now = DateTime.UtcNow;
        if (now < cert.NotBefore || now > cert.NotAfter)
            return new(null, CertValidationError.InvalidCertificate);

        var cn = cert.GetNameInfo(X509NameType.SimpleName, false);
        if (!Guid.TryParse(cn, out var collectorId))
            return new(null, CertValidationError.InvalidCertificate);

        if (!ValidateSan(cert, collectorId))
            return new(null, CertValidationError.InvalidCertificate);

        var thumbprint = ClientCertificateParser.ComputeThumbprint(cert);

        // Registry failures → 403
        var certRecord = await registry.GetCertificateByThumbprintAsync(thumbprint, ct);
        if (certRecord is null || certRecord.RevokedAt is not null)
            return new(null, CertValidationError.ForbiddenAccess);

        var collectorRecord = await registry.GetCollectorByThumbprintAsync(thumbprint, ct);
        if (collectorRecord is null || collectorRecord.Status != "Enrolled")
            return new(null, CertValidationError.ForbiddenAccess);

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

    private static bool ValidateSan(X509Certificate2 cert, Guid collectorId)
    {
        var expected = $"urn:mod:collector:{collectorId:D}";
        var sanExt = cert.Extensions["2.5.29.17"];
        if (sanExt is null)
            return false;

        foreach (var uri in ReadSanUris(sanExt.RawData))
        {
            if (string.Equals(uri, expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // GeneralName [6] uniformResourceIdentifier ::= IMPLICIT IA5String
    private static List<string> ReadSanUris(byte[] rawData)
    {
        var result = new List<string>();
        try
        {
            var uriTag = new Asn1Tag(TagClass.ContextSpecific, 6);
            var reader = new AsnReader(rawData, AsnEncodingRules.DER);
            var seq = reader.ReadSequence();
            while (seq.HasData)
            {
                var tag = seq.PeekTag();
                if (tag.TagClass == TagClass.ContextSpecific && tag.TagValue == 6 && !tag.IsConstructed)
                    result.Add(seq.ReadCharacterString(UniversalTagNumber.IA5String, uriTag));
                else
                    seq.ReadEncodedValue();
            }
        }
        catch (AsnContentException) { }
        return result;
    }
}
