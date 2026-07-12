using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Tests.Infrastructure;

// Runs against the real local dev Postgres (docker-compose.yml) — the backfill logic is raw
// SQL that the in-memory provider never executes, so this is the only way to actually prove the
// migration ordering (nullable -> backfill -> verify -> non-nullable) works end to end.
public sealed class TenantBackfillIntegrationTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    [Fact]
    public async Task Migration_DefaultTenantExists_WithNoApiKeys()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            "SELECT \"Status\" FROM \"Tenants\" WHERE \"Id\" = '00000000-0000-0000-0000-000000000001'", connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            (await reader.ReadAsync()).Should().BeTrue("the default tenant must exist after migration");
            reader.GetString(0).Should().Be("Active");
        }

        await using var keyCmd = new NpgsqlCommand(
            "SELECT count(*) FROM \"ApiKeys\" WHERE \"TenantId\" = '00000000-0000-0000-0000-000000000001'", connection);
        var keyCount = (long)(await keyCmd.ExecuteScalarAsync())!;
        keyCount.Should().Be(0, "the default/system tenant must never have a valid API key");
    }

    [Theory]
    [InlineData("OrchestrationTasks")]
    [InlineData("AgentExecutions")]
    [InlineData("AgentMemories")]
    [InlineData("AgentMessages")]
    [InlineData("AgentRetryAttempts")]
    [InlineData("CostLedger")]
    [InlineData("CostRollups")]
    [InlineData("McpToolCalls")]
    [InlineData("TaskCheckpoints")]
    [InlineData("EvalSuites")]
    [InlineData("EvalCases")]
    [InlineData("EvalRuns")]
    [InlineData("EvalResults")]
    public async Task Table_HasNoNullTenantIds(string tableName)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM \"{tableName}\" WHERE \"TenantId\" IS NULL", connection);
        var nullCount = (long)(await cmd.ExecuteScalarAsync())!;

        nullCount.Should().Be(0, $"{tableName}.TenantId must be fully backfilled with no nulls remaining");
    }

    [Fact]
    public async Task EvalCase_InheritsTenantFromItsSuite_ForExistingBackfilledData()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            SELECT count(*) FROM "EvalCases" ec
            JOIN "EvalSuites" es ON ec."SuiteId" = es."Id"
            WHERE ec."TenantId" <> es."TenantId"
            """, connection);
        var mismatches = (long)(await cmd.ExecuteScalarAsync())!;

        mismatches.Should().Be(0, "every EvalCase's TenantId must match its owning EvalSuite's TenantId — no independent per-table assignment");
    }

    [Fact]
    public async Task ApiKeys_CheckConstraint_RejectsDefaultTenantId_EvenViaRawSql()
    {
        // Proves the "default tenant can never authenticate" invariant holds at the schema
        // level, not only inside CreateApiKeyHandler (Task 8) — a raw INSERT that bypasses the
        // application layer entirely must still be refused by the database itself.
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "ApiKeys" ("Id", "TenantId", "PublicKeyId", "HashedSecret", "CreatedAt")
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000001', 'pk_should_never_exist', 'hash', now())
            """, connection);

        var act = async () => await cmd.ExecuteNonQueryAsync();

        var exception = await act.Should().ThrowAsync<PostgresException>();
        exception.Which.SqlState.Should().Be("23514", "23514 is Postgres's check_violation error code");
        exception.Which.ConstraintName.Should().Be("CK_ApiKeys_TenantId_NotDefault");
    }
}
