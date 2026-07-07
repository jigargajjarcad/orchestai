namespace OrchestAI.Infrastructure.Tools;

public interface IDatabaseQueryExecutor
{
    Task<DatabaseQueryResult> ExecuteAsync(
        string connectionString,
        string query,
        int timeoutSeconds,
        int maxRows,
        CancellationToken cancellationToken = default);
}

public sealed record DatabaseQueryResult(
    bool Success,
    string? Json,
    int RowCount,
    bool Truncated,
    string? ErrorMessage
);

public enum DatabaseProvider
{
    Postgres,
    SqlServer
}
