using Microsoft.Data.SqlClient;

namespace DMS.Integration.Mes.Database;

public sealed class MesSqlConnectionFactory
    : IMesSqlConnectionFactory
{
    public string BuildConnectionString(
        MesDatabaseConnectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        var builder =
            new SqlConnectionStringBuilder
            {
                DataSource = settings.Server,
                InitialCatalog = settings.Database,
                IntegratedSecurity = true,
                TrustServerCertificate = settings.TrustServerCertificate,
                ConnectTimeout = settings.ConnectTimeoutSeconds,
                ApplicationName = settings.ApplicationName,
                PersistSecurityInfo = false,
                Pooling = true
            };

        // SqlClient 7 exposes Encrypt as a richer option; using the keyword
        // through the builder indexer keeps the settings model simple.
        builder["Encrypt"] =
            settings.Encrypt
                ? "True"
                : "False";

        return builder.ConnectionString;
    }

    public async Task<SqlConnection> OpenAsync(
        MesDatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        var connection =
            new SqlConnection(
                BuildConnectionString(settings));

        try
        {
            await connection
                .OpenAsync(cancellationToken)
                .ConfigureAwait(false);

            return connection;
        }
        catch
        {
            await connection
                .DisposeAsync()
                .ConfigureAwait(false);

            throw;
        }
    }
}
