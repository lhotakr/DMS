using Microsoft.Data.SqlClient;

namespace DMS.Integration.Mes.Database;

public interface IMesSqlConnectionFactory
{
    string BuildConnectionString(
        MesDatabaseConnectionSettings settings);

    Task<SqlConnection> OpenAsync(
        MesDatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default);
}
