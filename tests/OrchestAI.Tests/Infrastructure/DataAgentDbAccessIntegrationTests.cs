using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OrchestAI.Infrastructure.Tools;

namespace OrchestAI.Tests.Infrastructure;

// Live-confirmation coverage for the P2-M4 DataAgent finding: the seeded PascalCase table is
// reachable through the real db_query path (DatabaseTool + AdoDatabaseQueryExecutor) ONLY when
// identifiers are double-quoted; the unquoted form fails exactly as observed in the genuine run
// (PostgreSQL folds unquoted identifiers to lower case). Deterministic — no LLM/paid call.
// Requires the local/CI Postgres with migrations applied; SportsPerformanceGames exists via
// migration, and COUNT(*) works regardless of whether the demo fixture rows are seeded.
public sealed class DataAgentDbAccessIntegrationTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    private static DatabaseTool CreateTool()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:default"] = ConnectionString
            })
            .Build();

        return new DatabaseTool(
            config,
            new AdoDatabaseQueryExecutor(NullLogger<AdoDatabaseQueryExecutor>.Instance),
            NullLogger<DatabaseTool>.Instance);
    }

    [Fact]
    public async Task DbQuery_QuotedPascalCaseTable_SucceedsAgainstSportsPerformanceGames()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "SELECT COUNT(*) AS n FROM \"SportsPerformanceGames\""
        });

        result.Success.Should().BeTrue(
            "the PascalCase fixture table is reachable via db_query when identifiers are double-quoted");
        result.Output.Should().Contain("n");
    }

    [Fact]
    public async Task DbQuery_UnquotedIdentifier_FailsExactlyAsObservedLive()
    {
        var tool = CreateTool();

        var result = await tool.ExecuteAsync(new Dictionary<string, string>
        {
            ["query"] = "SELECT COUNT(*) FROM SportsPerformanceGames"
        });

        result.Success.Should().BeFalse(
            "PostgreSQL folds the unquoted identifier to lower case, so it does not match the PascalCase table");
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
