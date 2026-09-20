using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Mod.ReportingApi.Auth;

namespace Mod.ReportingApi.Data;

public sealed class TenantSessionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        SetSessionContext(connection);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        await SetSessionContextAsync(connection, ct);
    }

    private void SetSessionContext(DbConnection connection)
    {
        if (tenant.TenantId == Guid.Empty)
            throw new InvalidOperationException("Tenant context is not set; cannot open a database connection without a tenant.");

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context @key=N'TenantId', @value=@t, @read_only=1";
        var p = cmd.CreateParameter();
        p.ParameterName = "@t";
        p.Value = tenant.TenantId;
        cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    private async Task SetSessionContextAsync(DbConnection connection, CancellationToken ct)
    {
        if (tenant.TenantId == Guid.Empty)
            throw new InvalidOperationException("Tenant context is not set; cannot open a database connection without a tenant.");

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context @key=N'TenantId', @value=@t, @read_only=1";
        var p = cmd.CreateParameter();
        p.ParameterName = "@t";
        p.Value = tenant.TenantId;
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
