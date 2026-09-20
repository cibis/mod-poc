using Mod.ManagementApi.Auth;
using Mod.ManagementApi.Config;
using Mod.Platform.Common.Certificates;

namespace Mod.ManagementApi.Endpoints;

public static class ConfigEndpoint
{
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        CertificateValidator certValidator,
        ConfigDocumentBuilder configBuilder,
        CancellationToken ct)
    {
        var (err, identity) = await CertAuth.AuthenticateAsync(context, certValidator, ct);
        if (err is not null) return err;

        var config = await configBuilder.BuildAsync(identity!.CollectorId, ct);
        if (config is null)
            return Results.Problem(statusCode: 404, type: "CollectorNotFound");

        var etag = $"\"{config.ConfigVersion}\"";

        var ifNoneMatch = context.Request.Headers.IfNoneMatch.ToString();
        if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
            return Results.StatusCode(304);

        context.Response.Headers.ETag = etag;
        return Results.Ok(config);
    }
}
