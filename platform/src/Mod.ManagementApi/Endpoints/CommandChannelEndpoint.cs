using Dapper;
using Mod.ManagementApi.Auth;
using Mod.ManagementApi.ServiceBus;
using Mod.Platform.Common.Certificates;
using Mod.Platform.Common.Sql;

namespace Mod.ManagementApi.Endpoints;

public static class CommandChannelEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        CertificateValidator certValidator,
        SasTokenFactory sasFactory,
        SqlConnectionFactory db,
        CancellationToken ct)
    {
        var (err, identity) = await CertAuth.AuthenticateAsync(context, certValidator, ct);
        if (err is not null) return err;

        await using var conn = db.CreateConnection();
        var row = await conn.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                "SELECT CommandChannelEnabled, TenantId FROM registry.Collector WHERE CollectorId = @id",
                new { id = identity!.CollectorId }, cancellationToken: ct));

        if (row is null)
            return Results.Problem(statusCode: 404, type: "CollectorNotFound");

        if (!(bool)row.CommandChannelEnabled)
            return Results.Ok(new { enabled = false });

        var tokens = sasFactory.MintCollectorTokens((Guid)row.TenantId, identity.CollectorId);

        return Results.Ok(new
        {
            enabled = true,
            namespaceFqdn = tokens.NamespaceFqdn,
            requestQueue = tokens.RequestQueue,
            replyQueue = tokens.ReplyQueue,
            requestSasToken = tokens.RequestSasToken,
            replySasToken = tokens.ReplySasToken,
            expiresAt = tokens.ExpiresAt.ToString("O")
        });
    }
}
