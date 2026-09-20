using Dapper;
using Microsoft.Data.SqlClient;

namespace Mod.DbMigrator.Steps;

internal sealed class UsersStep(string connectionString)
{
    private static readonly (string EnvSuffix, string Role)[] ServiceRoles =
    [
        ("INGEST",     "role_ingest"),
        ("PROCESSING", "role_processing"),
        ("MANAGEMENT", "role_management"),
        ("PORTAL",     "role_portal"),
        ("REPORTING",  "role_reporting"),
    ];

    public async Task RunAsync()
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        foreach (var (envSuffix, role) in ServiceRoles)
        {
            var name     = Environment.GetEnvironmentVariable($"IDENTITY_{envSuffix}_NAME");
            var objectId = Environment.GetEnvironmentVariable($"IDENTITY_{envSuffix}_OBJECT_ID");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(objectId))
            {
                Console.WriteLine($"  IDENTITY_{envSuffix}_NAME / _OBJECT_ID not set – skipping.");
                continue;
            }
            if (!Guid.TryParse(objectId, out var guidValue))
                throw new InvalidOperationException(
                    $"IDENTITY_{envSuffix}_OBJECT_ID is not a valid GUID: {objectId}");

            await EnsureUserAsync(conn, name, guidValue);
            await EnsureRoleMemberAsync(conn, name, role);
            Console.WriteLine($"  {name} → {role}");
        }

        var devPrincipals = Environment.GetEnvironmentVariable("DEVELOPER_PRINCIPALS");
        if (string.IsNullOrWhiteSpace(devPrincipals))
            return;

        foreach (var entry in devPrincipals.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split(':', 2);
            if (parts.Length != 2)
                throw new InvalidOperationException(
                    $"DEVELOPER_PRINCIPALS entry '{entry}' must be 'name:objectId'.");

            var name     = parts[0].Trim();
            var objectId = parts[1].Trim();
            if (!Guid.TryParse(objectId, out var guidValue))
                throw new InvalidOperationException(
                    $"DEVELOPER_PRINCIPALS objectId is not a valid GUID: {objectId}");

            await EnsureUserAsync(conn, name, guidValue);
            await EnsureRoleMemberAsync(conn, name, "db_datareader");
            await EnsureRoleMemberAsync(conn, name, "db_datawriter");
            Console.WriteLine($"  {name} → db_datareader + db_datawriter");
        }
    }

    private static async Task EnsureUserAsync(SqlConnection conn, string name, Guid objectId)
    {
        var exists = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sys.database_principals WHERE name = @name",
            new { name });

        if (exists == 0)
        {
            var safeName = name.Replace("]", "]]");
            var safeId   = objectId.ToString("D");
            await conn.ExecuteAsync(
                $"CREATE USER [{safeName}] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '{safeId}'");
        }
    }

    private static async Task EnsureRoleMemberAsync(SqlConnection conn, string userName, string role)
    {
        var isMember = await conn.ExecuteScalarAsync<int>(
            "SELECT IS_ROLEMEMBER(@role, @userName)",
            new { role, userName });

        if (isMember != 1)
        {
            var safeRole = role.Replace("]", "]]");
            var safeUser = userName.Replace("]", "]]");
            await conn.ExecuteAsync($"ALTER ROLE [{safeRole}] ADD MEMBER [{safeUser}]");
        }
    }
}
