using Microsoft.Data.SqlClient;

namespace Mod.Platform.Common.Sql;

public sealed class SqlConnectionFactory(string connectionString)
{
    public SqlConnection CreateConnection() => new(connectionString);
}
