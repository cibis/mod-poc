using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Mod.ManagementApi.Config;
using Mod.ManagementApi.Models;
using Mod.ManagementApi.Pki;
using Mod.Platform.Common.Certificates;
using Mod.Platform.Common.Sql;

namespace Mod.ManagementApi.Endpoints;

public static class EnrolEndpoint
{
    public static async Task<IResult> HandleAsync(
        EnrolRequest request,
        CertificateAuthority ca,
        ConfigDocumentBuilder configBuilder,
        SqlConnectionFactory db,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var log = loggerFactory.CreateLogger("Mod.ManagementApi.Enrol");

        if (string.IsNullOrWhiteSpace(request.EnrolmentToken) || string.IsNullOrWhiteSpace(request.CsrPem))
            return Results.Problem(statusCode: 400, type: "EnrolmentTokenInvalid");

        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(request.EnrolmentToken));

        await using var conn = db.CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, ct);

        // Lookup token with UPDLOCK to prevent double-use
        const string lookupSql = """
            SELECT et.CollectorId, et.UsedAt, et.ExpiresAt, c.Status, c.TenantId, c.SiteId
            FROM registry.EnrolmentToken et WITH (UPDLOCK)
            JOIN registry.Collector c ON c.CollectorId = et.CollectorId
            WHERE et.TokenHash = @tokenHash
            """;

        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(lookupSql, new { tokenHash }, tx, cancellationToken: ct));

        if (row is null)
        {
            await tx.RollbackAsync(ct);
            return Results.Problem(statusCode: 401, type: "EnrolmentTokenInvalid");
        }

        string status = (string)row.Status;
        Guid collectorId = (Guid)row.CollectorId;
        Guid tenantId = (Guid)row.TenantId;

        // Collector status check (409 before 401 so portal can distinguish)
        if (status is "Revoked" or "Retired")
        {
            await WriteAuditAsync(conn, tx, collectorId, tenantId, "EnrolmentRejected", ct);
            await tx.CommitAsync(ct);
            return Results.Problem(statusCode: 409, type: "CollectorNotEnrollable");
        }

        // Token validity
        DateTime? usedAt = (DateTime?)row.UsedAt;
        DateTime expiresAt = (DateTime)row.ExpiresAt;
        if (usedAt is not null || expiresAt < DateTime.UtcNow)
        {
            await WriteAuditAsync(conn, tx, collectorId, tenantId, "EnrolmentRejected", ct);
            await tx.CommitAsync(ct);
            return Results.Problem(statusCode: 401, type: "EnrolmentTokenInvalid");
        }

        // Issue certificate
        var (issuedCert, csrError) = ca.IssueCertificate(request.CsrPem, collectorId);
        if (csrError is not null || issuedCert is null)
        {
            await tx.RollbackAsync(ct);
            return Results.Problem(statusCode: 400, type: "CsrInvalid");
        }

        var thumbprint = ClientCertificateParser.ComputeThumbprint(issuedCert);
        var certPem = issuedCert.ExportCertificatePem();
        var certExpiresAt = issuedCert.NotAfter;
        issuedCert.Dispose();

        var now = DateTime.UtcNow;

        // Mark token used
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE registry.EnrolmentToken SET UsedAt = @now WHERE TokenHash = @tokenHash",
                new { now, tokenHash }, tx, cancellationToken: ct));

        // Revoke all existing non-revoked certs
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE registry.Certificate SET RevokedAt = @now WHERE CollectorId = @collectorId AND RevokedAt IS NULL",
                new { now, collectorId }, tx, cancellationToken: ct));

        // Insert new certificate
        await conn.ExecuteAsync(
            new CommandDefinition("""
                INSERT INTO registry.Certificate
                    (Thumbprint, CollectorId, SerialNumber, IssuedAt, ExpiresAt)
                VALUES (@thumbprint, @collectorId, @serialNumber, @issuedAt, @expiresAt)
                """,
                new
                {
                    thumbprint,
                    collectorId,
                    serialNumber = thumbprint[..16],
                    issuedAt = now,
                    expiresAt = certExpiresAt
                }, tx, cancellationToken: ct));

        // Update collector
        await conn.ExecuteAsync(
            new CommandDefinition(
                "UPDATE registry.Collector SET Status = 'Enrolled', EnrolledAt = @now, SoftwareVersion = @sv WHERE CollectorId = @collectorId",
                new { now, sv = request.SoftwareVersion, collectorId }, tx, cancellationToken: ct));

        // Audit
        await WriteAuditAsync(conn, tx, collectorId, tenantId, "CollectorEnrolled", ct);

        await tx.CommitAsync(ct);

        // Build config document (outside transaction)
        var config = await configBuilder.BuildAsync(collectorId, ct);
        if (config is null)
            return Results.Problem(statusCode: 500, type: "InternalError");

        log.LogInformation("CollectorEnrolled: collectorId={CollectorId} tenantId={TenantId}", collectorId, tenantId);

        return Results.Ok(new
        {
            collectorId = collectorId.ToString("D"),
            tenantId = tenantId.ToString("D"),
            siteId = ((Guid)row.SiteId).ToString("D"),
            certificatePem = certPem,
            caCertificatePem = ca.CaCertificatePem,
            certificateExpiresAt = certExpiresAt.ToString("O"),
            config
        });
    }

    private static Task WriteAuditAsync(
        SqlConnection conn,
        SqlTransaction tx,
        Guid collectorId,
        Guid tenantId,
        string action,
        CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO registry.AuditLog (At, ActorName, ActorKind, Action, TargetType, TargetId, TenantId)
            VALUES (@at, @actorName, 'Collector', @action, 'Collector', @targetId, @tenantId)
            """,
            new
            {
                at = DateTime.UtcNow,
                actorName = collectorId.ToString("D"),
                action,
                targetId = collectorId.ToString("D"),
                tenantId
            }, tx, cancellationToken: ct));
}
