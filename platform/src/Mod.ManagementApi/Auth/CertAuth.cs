using Mod.Platform.Common.Certificates;

namespace Mod.ManagementApi.Auth;

internal static class CertAuth
{
    public static async Task<(IResult? Error, CollectorIdentity? Identity)> AuthenticateAsync(
        HttpContext context,
        CertificateValidator validator,
        CancellationToken ct)
    {
        var header = context.Request.Headers["X-Forwarded-Client-Cert"].ToString();
        var cert = ClientCertificateParser.Parse(header);
        if (cert is null)
            return (Results.StatusCode(401), null);

        // PoC: collectorId is trusted from X-Collector-Id header after CA chain validation.
        // Production: extract collectorId from cert CN, never from a header.
        var collectorIdHint = context.Request.Headers["X-Collector-Id"].ToString();

        CertValidationResult result;
        try
        {
            result = await validator.ValidateAsync(cert, collectorIdHint, ct);
        }
        finally
        {
            cert.Dispose();
        }

        if (!result.IsSuccess)
        {
            return result.Error == CertValidationError.ForbiddenAccess
                ? (Results.StatusCode(403), null)
                : (Results.StatusCode(401), null);
        }

        return (null, result.Identity!);
    }
}
