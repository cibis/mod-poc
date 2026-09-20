using Dapper;
using Microsoft.Data.SqlClient;
using Mod.ManagementApi.Auth;
using Mod.ManagementApi.Pki;
using Mod.Platform.Common.Certificates;
using Mod.Platform.Common.Sql;

namespace Mod.ManagementApi.Endpoints;

public static class RenewEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        RenewRequest request,
        CertificateAuthority ca,
        CertificateValidator certValidator,
        SqlConnectionFactory db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var (err, identity) = await CertAuth.AuthenticateAsync(context, certValidator, ct);
        if (err is not null) return err;

        var (issuedCert, csrError) = ca.IssueCertificate(request.CsrPem, identity!.CollectorId);
        if (csrError is not null || issuedCert is null)
            return Results.Problem(statusCode: 400, type: "CsrInvalid");

        var thumbprint = ClientCertificateParser.ComputeThumbprint(issuedCert);
        var certPem = issuedCert.ExportCertificatePem();
        var certExpiresAt = issuedCert.NotAfter;
        issuedCert.Dispose();

        var now = DateTime.UtcNow;

        await using var conn = db.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, ct);

        // Insert new certificate
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO registry.Certificate
                (Thumbprint, CollectorId, SerialNumber, IssuedAt, ExpiresAt)
            VALUES (@thumbprint, @collectorId, @serialNumber, @issuedAt, @expiresAt)
            """,
            new
            {
                thumbprint,
                collectorId = identity.CollectorId,
                serialNumber = thumbprint[..16],
                issuedAt = now,
                expiresAt = certExpiresAt
            }, tx, cancellationToken: ct));

        // Supersede previous certificates (stays valid until expiry — renewals only)
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE registry.Certificate SET SupersededAt = @now WHERE CollectorId = @collectorId AND SupersededAt IS NULL AND Thumbprint != @thumbprint",
            new { now, collectorId = identity.CollectorId, thumbprint }, tx, cancellationToken: ct));

        // Audit
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO registry.AuditLog (At, ActorName, ActorKind, Action, TargetType, TargetId, TenantId)
            VALUES (@at, @actorName, 'Collector', 'CertificateRenewed', 'Collector', @targetId, @tenantId)
            """,
            new
            {
                at = now,
                actorName = identity.CollectorId.ToString("D"),
                targetId = identity.CollectorId.ToString("D"),
                tenantId = identity.TenantId
            }, tx, cancellationToken: ct));

        await tx.CommitAsync(ct);

        loggerFactory.CreateLogger("Mod.ManagementApi.Renew")
            .LogInformation("CertificateRenewed: collectorId={CollectorId}", identity.CollectorId);

        return Results.Ok(new
        {
            certificatePem = certPem,
            certificateExpiresAt = certExpiresAt.ToString("O")
        });
    }
}

public sealed class RenewRequest
{
    public string CsrPem { get; init; } = "";
}
