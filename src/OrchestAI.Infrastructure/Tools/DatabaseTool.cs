using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Tools;

public sealed partial class DatabaseTool : IMcpTool
{
    private const int TimeoutSeconds = 30;
    private const int MaxRows = 500;

    private readonly IConfiguration _configuration;
    private readonly IDatabaseQueryExecutor _executor;
    private readonly ILogger<DatabaseTool> _logger;

    public string ToolName => "db_query";

    public string Description =>
        "Execute a read-only SQL query against a configured database. " +
        "Supports PostgreSQL and SQL Server. Returns results as JSON.";

    public DatabaseTool(IConfiguration configuration, IDatabaseQueryExecutor executor, ILogger<DatabaseTool> logger)
    {
        _configuration = configuration;
        _executor = executor;
        _logger = logger;
    }

    public ToolInputSchema GetInputSchema() => new(
        Type: "object",
        Properties: new Dictionary<string, ToolProperty>
        {
            ["query"] = new("string", "The read-only SQL SELECT query to execute"),
            ["database"] = new("string", "Named connection string to query — defaults to 'default'")
        },
        Required: ["query"]
    );

    public async Task<McpToolResult> ExecuteAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken = default)
    {
        if (!parameters.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            return new McpToolResult(false, string.Empty, "Missing required parameter: query");

        if (IsWriteQuery(query))
        {
            _logger.LogWarning("DatabaseTool rejected a write/DDL query");
            return new McpToolResult(false, string.Empty,
                "Query rejected: only read-only SELECT queries are allowed.");
        }

        var database = parameters.TryGetValue("database", out var db) && !string.IsNullOrWhiteSpace(db)
            ? db
            : "default";

        var connectionString = _configuration.GetConnectionString(database);
        if (string.IsNullOrWhiteSpace(connectionString))
            return new McpToolResult(false, string.Empty, $"Connection string '{database}' is not configured");

        var result = await _executor
            .ExecuteAsync(connectionString, query, TimeoutSeconds, MaxRows, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
            return new McpToolResult(false, string.Empty, result.ErrorMessage ?? "Query failed");

        _logger.LogInformation(
            "DatabaseTool query against '{Database}' returned {RowCount} rows (truncated={Truncated})",
            database, result.RowCount, result.Truncated);

        var output = result.Truncated
            ? $"{result.Json}\n[Note: results truncated to {MaxRows} rows]"
            : result.Json!;

        return new McpToolResult(true, output);
    }

    internal static bool IsWriteQuery(string query) => WriteQueryPattern().IsMatch(query);

    [GeneratedRegex(
        @"\b(INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|EXEC|EXECUTE)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex WriteQueryPattern();
}
