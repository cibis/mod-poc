using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace Mod.DbMigrator.Steps;

internal sealed class SeedStep(string connectionString, string adminPassword, string customerPassword)
{
    private static readonly Guid Ns = new("6f2c1a4e-9b1d-4c1e-8a57-3d0e2b7c9a10");
    private static readonly DateTime SeedAt = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] AssetTypes = ["Filler", "Capper", "Labeler", "Packer"];

    // Placeholder structure; management-api task defines the real ConfigDocument defaults.
    private const string DefaultSettingsJson =
        """{"batching":{},"forwarder":{},"buffer":{},"intervals":{}}""";

    private record CollectorDef(string Name, string V5Path, int[] Lines);
    private record SiteDef(string Name, string RegionLabel, CollectorDef[] Collectors);
    private record TenantDef(string Slug, string DisplayName, SiteDef[] Sites);

    private static readonly TenantDef[] Data =
    [
        new("customer-a", "Customer A",
        [
            new("Customer A North 1", "EU-North",
            [
                new("Customer A North 1 Collector", "collector:customer-a/Customer A North 1", [1, 2]),
            ]),
            new("Customer A North 2", "EU-North",
            [
                new("Customer A North 2 Collector", "collector:customer-a/Customer A North 2", [1, 2]),
            ]),
            new("Customer A South 1", "EU-South",
            [
                new("Customer A South 1 Collector", "collector:customer-a/Customer A South 1", [1, 2]),
            ]),
        ]),
        new("customer-b", "Customer B",
        [
            new("Customer B North 1", "EU-North",
            [
                new("Customer B North 1 Collector", "collector:customer-b/Customer B North 1", [1, 2]),
            ]),
            new("Customer B South 1", "EU-South",
            [
                new("Customer B South 1 Collector", "collector:customer-b/Customer B South 1", [1, 2]),
            ]),
        ]),
        new("customer-c", "Customer C",
        [
            new("Customer C North 1", "EU-North",
            [
                new("Customer C North 1 Collector", "collector:customer-c/Customer C North 1", [1, 2]),
            ]),
            new("Customer C South 1", "EU-South",
            [
                new("Customer C South 1 Line 1 Collector", "collector:customer-c/Customer C South 1/Line 1", [1]),
                new("Customer C South 1 Line 2 Collector", "collector:customer-c/Customer C South 1/Line 2", [2]),
            ]),
        ]),
    ];

    public async Task RunAsync()
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var hasher = new PasswordHasher<string>();

        foreach (var tenant in Data)
        {
            var tenantId = V5($"tenant:{tenant.Slug}");

            if (!await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [registry].[Tenant] WHERE TenantId = @id", tenantId))
                await conn.ExecuteAsync(
                    """
                    INSERT INTO [registry].[Tenant] (TenantId, Name, Slug, IsolationMode, Status, CreatedAt)
                    VALUES (@TenantId, @Name, @Slug, 'Pooled', 'Active', @CreatedAt)
                    """,
                    new { TenantId = tenantId, Name = tenant.DisplayName, Slug = tenant.Slug, CreatedAt = SeedAt });

            foreach (var site in tenant.Sites)
            {
                var siteId = V5($"site:{tenant.Slug}/{site.Name}");

                if (!await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [registry].[Site] WHERE SiteId = @id", siteId))
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO [registry].[Site] (SiteId, TenantId, Name, RegionLabel, TimeZone, CreatedAt)
                        VALUES (@SiteId, @TenantId, @Name, @RegionLabel, 'Europe/Berlin', @CreatedAt)
                        """,
                        new { SiteId = siteId, TenantId = tenantId, Name = site.Name, RegionLabel = site.RegionLabel, CreatedAt = SeedAt });

                for (var li = 1; li <= 2; li++)
                {
                    var lineName = $"Line {li}";
                    var lineId = V5($"line:{tenant.Slug}/{site.Name}/{lineName}");

                    if (!await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [registry].[Line] WHERE LineId = @id", lineId))
                        await conn.ExecuteAsync(
                            """
                            INSERT INTO [registry].[Line] (LineId, TenantId, SiteId, Name, CreatedAt)
                            VALUES (@LineId, @TenantId, @SiteId, @Name, @CreatedAt)
                            """,
                            new { LineId = lineId, TenantId = tenantId, SiteId = siteId, Name = lineName, CreatedAt = SeedAt });

                    for (var ai = 0; ai < AssetTypes.Length; ai++)
                    {
                        var assetType = AssetTypes[ai];
                        var assetId = V5($"asset:{tenant.Slug}/{site.Name}/{lineName}/{assetType}");

                        if (!await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [registry].[Asset] WHERE AssetId = @id", assetId))
                            await conn.ExecuteAsync(
                                """
                                INSERT INTO [registry].[Asset]
                                    (AssetId, TenantId, SiteId, LineId, Name, AssetType,
                                     CounterName, ProcessValueName, ProcessValueUnit, ProcessValueMin, ProcessValueMax, CreatedAt)
                                VALUES (@AssetId, @TenantId, @SiteId, @LineId, @Name, @AssetType,
                                        'good_count', 'speed', 'units/min', 0.0, 600.0, @CreatedAt)
                                """,
                                new
                                {
                                    AssetId = assetId, TenantId = tenantId, SiteId = siteId, LineId = lineId,
                                    Name = $"{lineName} {assetType}", AssetType = assetType, CreatedAt = SeedAt,
                                });
                    }
                }

                foreach (var coll in site.Collectors)
                {
                    var collId = V5(coll.V5Path);

                    if (!await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [registry].[Collector] WHERE CollectorId = @id", collId))
                        await conn.ExecuteAsync(
                            """
                            INSERT INTO [registry].[Collector]
                                (CollectorId, TenantId, SiteId, Name, Status, CommandChannelEnabled, CreatedAt)
                            VALUES (@CollectorId, @TenantId, @SiteId, @Name, 'Registered', 1, @CreatedAt)
                            """,
                            new { CollectorId = collId, TenantId = tenantId, SiteId = siteId, Name = coll.Name, CreatedAt = SeedAt });

                    if (!await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [registry].[CollectorConfig] WHERE CollectorId = @id", collId))
                        await conn.ExecuteAsync(
                            """
                            INSERT INTO [registry].[CollectorConfig] (CollectorId, Version, SettingsJson, UpdatedAt, UpdatedBy)
                            VALUES (@CollectorId, 1, @SettingsJson, @UpdatedAt, 'seed')
                            """,
                            new { CollectorId = collId, SettingsJson = DefaultSettingsJson, UpdatedAt = SeedAt });

                    foreach (var lineIndex in coll.Lines)
                    {
                        var lineName = $"Line {lineIndex}";

                        for (var ai = 0; ai < AssetTypes.Length; ai++)
                        {
                            var assetType = AssetTypes[ai];
                            var assetId = V5($"asset:{tenant.Slug}/{site.Name}/{lineName}/{assetType}");
                            var sourceId = $"L{lineIndex}-A{ai + 1}";

                            var mappingExists = await conn.ExecuteScalarAsync<int>(
                                """
                                SELECT COUNT(1) FROM [registry].[AssetSourceMapping]
                                WHERE CollectorId = @CollectorId AND SourceId = @SourceId AND ValidFrom = @ValidFrom
                                """,
                                new { CollectorId = collId, SourceId = sourceId, ValidFrom = SeedAt });

                            if (mappingExists == 0)
                                await conn.ExecuteAsync(
                                    """
                                    INSERT INTO [registry].[AssetSourceMapping]
                                        (CollectorId, SourceId, AssetId, TenantId, ValidFrom, ValidTo)
                                    VALUES (@CollectorId, @SourceId, @AssetId, @TenantId, @ValidFrom, NULL)
                                    """,
                                    new { CollectorId = collId, SourceId = sourceId, AssetId = assetId, TenantId = tenantId, ValidFrom = SeedAt });
                        }
                    }

                    if (!await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [sim].[CollectorState] WHERE CollectorId = @id", collId))
                        await conn.ExecuteAsync(
                            """
                            INSERT INTO [sim].[CollectorState]
                                (CollectorId, PoweredOn, ContainerAppName, ProvisioningState,
                                 LastError, BufferOverflowEver, LastStatusJson, LastStatusAt, UpdatedAt)
                            VALUES (@CollectorId, 0, NULL, 'Off', NULL, 0, NULL, NULL, @UpdatedAt)
                            """,
                            new { CollectorId = collId, UpdatedAt = SeedAt });
                }
            }
        }

        await SeedUserAsync(conn, hasher, "admin",            adminPassword,    "ModAdmin", null,                       "Administrator");
        await SeedUserAsync(conn, hasher, "customer-a-viewer", customerPassword, "Customer", V5("tenant:customer-a"), "Customer A Viewer");
        await SeedUserAsync(conn, hasher, "customer-b-viewer", customerPassword, "Customer", V5("tenant:customer-b"), "Customer B Viewer");
        await SeedUserAsync(conn, hasher, "customer-c-viewer", customerPassword, "Customer", V5("tenant:customer-c"), "Customer C Viewer");
    }

    private static async Task SeedUserAsync(
        SqlConnection conn, PasswordHasher<string> hasher,
        string userName, string password, string kind,
        Guid? tenantId, string displayName)
    {
        var userId = V5($"user:{userName}");
        if (await ExistsByIdAsync(conn, "SELECT COUNT(1) FROM [registry].[AppUser] WHERE UserId = @id", userId))
            return;

        await conn.ExecuteAsync(
            """
            INSERT INTO [registry].[AppUser]
                (UserId, UserName, PasswordHash, Kind, TenantId, DisplayName, CreatedAt)
            VALUES (@UserId, @UserName, @PasswordHash, @Kind, @TenantId, @DisplayName, @CreatedAt)
            """,
            new
            {
                UserId = userId, UserName = userName,
                PasswordHash = hasher.HashPassword(userName, password),
                Kind = kind, TenantId = tenantId, DisplayName = displayName, CreatedAt = SeedAt,
            });
    }

    private static async Task<bool> ExistsByIdAsync(SqlConnection conn, string sql, Guid id)
    {
        var count = await conn.ExecuteScalarAsync<int>(sql, new { id });
        return count > 0;
    }

    // UUID v5 per RFC 4122, namespace 6f2c1a4e-9b1d-4c1e-8a57-3d0e2b7c9a10
    private static Guid V5(string name)
    {
        var nsBytes = ToRfc4122Bytes(Ns);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        nsBytes.CopyTo(input, 0);
        nameBytes.CopyTo(input, nsBytes.Length);

        var hash = SHA1.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50);   // version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);   // variant RFC 4122

        return FromRfc4122Bytes(hash[..16]);
    }

    // .NET Guid byte order → RFC 4122 big-endian (swap first 3 fields)
    private static byte[] ToRfc4122Bytes(Guid guid)
    {
        var b = guid.ToByteArray();
        Array.Reverse(b, 0, 4);
        Array.Reverse(b, 4, 2);
        Array.Reverse(b, 6, 2);
        return b;
    }

    private static Guid FromRfc4122Bytes(byte[] b)
    {
        var r = (byte[])b.Clone();
        Array.Reverse(r, 0, 4);
        Array.Reverse(r, 4, 2);
        Array.Reverse(r, 6, 2);
        return new Guid(r);
    }
}
