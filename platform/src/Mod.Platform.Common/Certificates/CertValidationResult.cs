namespace Mod.Platform.Common.Certificates;

public enum CertValidationError { InvalidCertificate, ForbiddenAccess }

public readonly record struct CertValidationResult(CollectorIdentity? Identity, CertValidationError? Error)
{
    public bool IsSuccess => Identity is not null;
}
