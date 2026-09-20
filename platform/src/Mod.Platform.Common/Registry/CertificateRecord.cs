namespace Mod.Platform.Common.Registry;

public record CertificateRecord(
    string Thumbprint,
    DateTimeOffset? RevokedAt);
