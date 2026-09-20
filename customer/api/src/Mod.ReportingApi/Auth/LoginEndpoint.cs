using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Mod.ReportingApi.Data;
using Mod.ReportingApi.Data.Entities;

namespace Mod.ReportingApi.Auth;

public static class LoginEndpoint
{
    public record LoginRequest(string UserName, string Password);

    private sealed record UserRow(
        Guid UserId, string UserName, string PasswordHash, string Kind,
        Guid? TenantId, string DisplayName, string? TenantName);

    public static async Task<IResult> Handle(
        LoginRequest req,
        LoginConnectionFactory factory,
        IConfiguration config,
        CancellationToken ct)
    {
        UserRow? row = null;
        await using (var conn = factory.Create())
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT u.UserId, u.UserName, u.PasswordHash, u.Kind,
                       u.TenantId, u.DisplayName, t.Name
                FROM registry.AppUser u
                LEFT JOIN registry.Tenant t ON t.TenantId = u.TenantId
                WHERE u.UserName = @u AND u.Kind = 'Customer'
                """;
            cmd.Parameters.AddWithValue("@u", req.UserName);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                row = new UserRow(
                    UserId: reader.GetGuid(0),
                    UserName: reader.GetString(1),
                    PasswordHash: reader.GetString(2),
                    Kind: reader.GetString(3),
                    TenantId: reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    DisplayName: reader.GetString(5),
                    TenantName: reader.IsDBNull(6) ? null : reader.GetString(6));
            }
        }

        if (row is null)
            return Unauthorized();

        var dummy = new AppUser();
        var hasher = new PasswordHasher<AppUser>();
        if (hasher.VerifyHashedPassword(dummy, row.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
            return Unauthorized();

        var key = config["REPORTING_JWT_KEY"]
            ?? throw new InvalidOperationException("REPORTING_JWT_KEY is required");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var creds = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddHours(8);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, row.UserId.ToString()),
            new("name", row.DisplayName),
            new("kind", row.Kind),
        };
        if (row.TenantId.HasValue)
            claims.Add(new Claim("tenant_id", row.TenantId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: "mod-poc-reporting",
            audience: "mod-poc-reporting",
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return Results.Ok(new
        {
            accessToken,
            expiresAt = expiresAt.ToString("O"),
            displayName = row.DisplayName,
            kind = row.Kind,
            tenantId = row.TenantId,
            tenantName = row.TenantName,
        });
    }

    private static IResult Unauthorized() =>
        Results.Problem(type: "InvalidCredentials", statusCode: StatusCodes.Status401Unauthorized);
}
