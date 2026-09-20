using Microsoft.Data.SqlClient;

namespace Mod.ReportingApi.Data;

public sealed class LoginConnectionFactory(IConfiguration config)
{
    public SqlConnection Create() =>
        new(config["SQL_CONNECTION"]
            ?? throw new InvalidOperationException("SQL_CONNECTION is required"));
}
