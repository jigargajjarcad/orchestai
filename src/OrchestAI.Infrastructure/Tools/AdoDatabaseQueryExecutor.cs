using System.Data.Common;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace OrchestAI.Infrastructure.Tools;

public sealed class AdoDatabaseQueryExecutor : IDatabaseQueryExecutor
{
    private readonly ILogger<AdoDatabaseQueryExecutor> _logger;

    public AdoDatabaseQueryExecutor(ILogger<AdoDatabaseQueryExecutor> logger) => _logger = logger;

    public async Task<DatabaseQueryResult> ExecuteAsync(
        string connectionString,
        string query,
        int timeoutSeconds,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = timeoutSeconds;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            var rows = new List<Dictionary<string, object?>>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (rows.Count >= maxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row[reader.GetName(i)] = value is DBNull ? null : value;
                }
                rows.Add(row);
            }

            var json = JsonSerializer.Serialize(rows);
            return new DatabaseQueryResult(true, json, rows.Count, truncated, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DatabaseQueryResult(false, null, 0, false, $"Query timed out after {timeoutSeconds} seconds");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database query failed");
            return new DatabaseQueryResult(false, null, 0, false, ex.Message);
        }
    }

    internal static DbConnection CreateConnection(string connectionString) =>
        DetectProvider(connectionString) == DatabaseProvider.SqlServer
            ? new SqlConnection(connectionString)
            : new NpgsqlConnection(connectionString);

    internal static DatabaseProvider DetectProvider(string connectionString) =>
        connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase)
            || connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            ? DatabaseProvider.SqlServer
            : DatabaseProvider.Postgres;
}
