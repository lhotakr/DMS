using Microsoft.Data.SqlClient;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DMS.Integration.Mes.Database;

public sealed partial class MesConnectionHealthService
{
    private readonly IMesSqlConnectionFactory _connectionFactory;

    public MesConnectionHealthService(
        IMesSqlConnectionFactory? connectionFactory = null)
    {
        _connectionFactory =
            connectionFactory
            ?? new MesSqlConnectionFactory();
    }

    public async Task<MesConnectionHealthResult> CheckAsync(
        MesDatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Normalize();

        if (!settings.IsEnabled)
        {
            return new MesConnectionHealthResult
            {
                IsEnabled = false,
                IsConnected = false,
                Server = settings.Server,
                Database = settings.Database,
                CheckedAt = DateTimeOffset.Now
            };
        }

        var stopwatch =
            Stopwatch.StartNew();

        try
        {
            await using var connection =
                await _connectionFactory
                    .OpenAsync(
                        settings,
                        cancellationToken)
                    .ConfigureAwait(false);

            var schema =
                ValidateIdentifier(
                    settings.ReportingSchema);

            var permissionObject =
                $"{schema}.DimWorkcenter";

            // SELECT is verified by executing a real read against the
            // reporting schema. This remains reliable even when metadata
            // visibility (VIEW DEFINITION) is intentionally restricted.
            await using (var selectProbe =
                         connection.CreateCommand())
            {
                selectProbe.CommandTimeout =
                    settings.CommandTimeoutSeconds;

                selectProbe.CommandText =
                    $"SELECT TOP (1) 1 FROM [{schema}].[DimWorkcenter];";

                _ =
                    await selectProbe
                        .ExecuteScalarAsync(
                            cancellationToken)
                        .ConfigureAwait(false);
            }

            var canSelect = true;

            await using var command =
                connection.CreateCommand();

            command.CommandTimeout =
                settings.CommandTimeoutSeconds;

            command.CommandText =
                """
                SELECT
                    DB_NAME() AS DatabaseName,
                    SUSER_SNAME() AS LoginName,
                    CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128)) AS ServerVersion,
                    HAS_PERMS_BY_NAME(@permissionObject, 'OBJECT', 'VIEW DEFINITION') AS CanViewDefinition;
                """;

            command.Parameters.AddWithValue(
                "@permissionObject",
                permissionObject);

            await using var reader =
                await command
                    .ExecuteReaderAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            string databaseName = settings.Database;
            string loginName = string.Empty;
            string serverVersion = string.Empty;
            var canViewDefinition = false;

            if (await reader
                    .ReadAsync(cancellationToken)
                    .ConfigureAwait(false))
            {
                databaseName =
                    reader.IsDBNull(0)
                        ? settings.Database
                        : reader.GetString(0);

                loginName =
                    reader.IsDBNull(1)
                        ? string.Empty
                        : reader.GetString(1);

                serverVersion =
                    reader.IsDBNull(2)
                        ? string.Empty
                        : reader.GetString(2);

                canViewDefinition =
                    !reader.IsDBNull(3)
                    && Convert.ToInt32(
                        reader.GetValue(3)) == 1;
            }

            stopwatch.Stop();

            return new MesConnectionHealthResult
            {
                IsEnabled = true,
                IsConnected = true,
                CanSelect = canSelect,
                CanViewDefinition = canViewDefinition,
                Server = settings.Server,
                Database = databaseName,
                LoginName = loginName,
                ServerVersion = serverVersion,
                LatencyMilliseconds = stopwatch.ElapsedMilliseconds,
                CheckedAt = DateTimeOffset.Now
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new MesConnectionHealthResult
            {
                IsEnabled = true,
                IsConnected = false,
                Server = settings.Server,
                Database = settings.Database,
                LatencyMilliseconds = stopwatch.ElapsedMilliseconds,
                CheckedAt = DateTimeOffset.Now,
                Error = ex.Message
            };
        }
    }

    internal static string ValidateIdentifier(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !SqlIdentifierRegex().IsMatch(value))
        {
            throw new InvalidOperationException(
                $"Invalid SQL identifier: {value}");
        }

        return value;
    }

    [GeneratedRegex(
        @"^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex SqlIdentifierRegex();
}
