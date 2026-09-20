using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Mod.PortalApi.Auth;

internal static class LoginEndpoint
{
    internal static IEndpointRouteBuilder MapLoginEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", Handle).AllowAnonymous();
        return app;
    }

    private static async Task<IResult> Handle(
        LoginRequest req,
        SqlConfig sqlConfig,
        JwtConfig jwtConfig)
    {
        AppUserRow? row;
        await using (var conn = new SqlConnection(sqlConfig.ConnectionString))
        {
            row = await conn.QuerySingleOrDefaultAsync<AppUserRow>(
                "SELECT UserId, DisplayName, PasswordHash, Kind, TenantId, TenantName " +
                "FROM registry.AppUser WHERE UserName = @UserName",
                new { req.UserName });
        }

        if (row is null)
            return Unauthorized();

        var hasher = new PasswordHasher<string>();
        if (hasher.VerifyHashedPassword(string.Empty, row.PasswordHash, req.Password)
            == PasswordVerificationResult.Failed)
            return Unauthorized();

        var expiresAt = DateTimeOffset.UtcNow.AddHours(8);
        var token = IssueToken(row, expiresAt, jwtConfig.Key);

        return Results.Ok(new LoginResponse(
            token,
            expiresAt,
            row.DisplayName,
            row.Kind,
            row.TenantId,
            row.TenantName));
    }

    private static string IssueToken(AppUserRow row, DateTimeOffset expiresAt, string keyBase64)
    {
        var key = new SymmetricSecurityKey(Convert.FromBase64String(keyBase64));
        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = row.UserId.ToString(),
            ["name"] = row.DisplayName,
            ["kind"] = row.Kind,
        };
        if (row.TenantId.HasValue)
            claims["tenant_id"] = row.TenantId.Value.ToString();

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = "mod-poc-portal",
            Audience = "mod-poc-portal",
            IssuedAt = DateTime.UtcNow,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static IResult Unauthorized() =>
        Results.Problem(type: "InvalidCredentials", statusCode: 401, title: "Invalid credentials.");

    private sealed record AppUserRow(
        Guid UserId,
        string DisplayName,
        string PasswordHash,
        string Kind,
        Guid? TenantId,
        string? TenantName);
}

internal sealed record LoginRequest(string UserName, string Password);

internal sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string DisplayName,
    string Kind,
    Guid? TenantId,
    string? TenantName);
