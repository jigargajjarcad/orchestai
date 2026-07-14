using FluentAssertions;
using Npgsql;

namespace OrchestAI.Tests.Infrastructure;

// Task 12 follow-up: proves the extended (Date, TenantId, UserId, AgentType, Model) unique index
// (migration ExtendCostRollupUniqueIndexWithTenantId) at the real Postgres level — the InMemory
// provider used by CostRollupRepositoryTests never enforces unique constraints, so that class can
// prove the repository's read/write behavior but not the database constraint itself. Runs against
// the real local dev Postgres (docker-compose.yml), same as TenantBackfillIntegrationTests. Every
// test wraps its inserts in a transaction that is always rolled back, so the shared dev database is
// left untouched regardless of pass/fail.
public sealed class CostRollupUniqueIndexIntegrationTests
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";

    [Fact]
    public async Task TwoTenants_SameDateUserAgentTypeModel_BothInsertsSucceed()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var userId = Guid.NewGuid();
            await InsertTenantAsync(connection, transaction, tenantA);
            await InsertTenantAsync(connection, transaction, tenantB);

            // Same (Date, UserId, AgentType, Model) — the exact tuple that collided under the old
            // (Date, UserId, AgentType, Model) unique index before TenantId was added to it. E.g.
            // two tenants' eval-suite-run rollups both attributed to DatabaseSeeder.EvalSystemUserId.
            var act = async () =>
            {
                await InsertCostRollupAsync(connection, transaction, tenantA, userId);
                await InsertCostRollupAsync(connection, transaction, tenantB, userId);
            };

            await act.Should().NotThrowAsync(
                "two different tenants' rollups for the same (Date, UserId, AgentType, Model) must not collide " +
                "on the extended (Date, TenantId, UserId, AgentType, Model) unique index");

            await using var countCmd = new NpgsqlCommand(
                "SELECT count(*) FROM \"CostRollups\" WHERE \"TenantId\" IN (@a, @b)", connection, transaction);
            countCmd.Parameters.AddWithValue("a", tenantA);
            countCmd.Parameters.AddWithValue("b", tenantB);
            var count = (long)(await countCmd.ExecuteScalarAsync())!;
            count.Should().Be(2);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    [Fact]
    public async Task SameTenant_DuplicateDateUserAgentTypeModel_ThrowsUniqueViolation()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        try
        {
            var tenantId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            await InsertTenantAsync(connection, transaction, tenantId);
            await InsertCostRollupAsync(connection, transaction, tenantId, userId);

            // Same tenant, same (Date, UserId, AgentType, Model) — a genuine duplicate rollup for
            // the SAME tenant must still be rejected; only the cross-tenant case should be allowed.
            var act = async () => await InsertCostRollupAsync(connection, transaction, tenantId, userId);

            var exception = await act.Should().ThrowAsync<PostgresException>();
            exception.Which.SqlState.Should().Be("23505", "23505 is Postgres's unique_violation error code");
            exception.Which.ConstraintName.Should().Be("IX_CostRollups_Date_TenantId_UserId_AgentType_Model");
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task InsertTenantAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "Tenants" ("Id", "Name", "Slug", "Status", "CreatedAt")
            VALUES (@id, 'Test Tenant', @slug, 'Active', now())
            """, connection, transaction);
        cmd.Parameters.AddWithValue("id", tenantId);
        cmd.Parameters.AddWithValue("slug", $"test-tenant-{tenantId:N}");
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertCostRollupAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid tenantId, Guid userId)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO "CostRollups"
                ("Id", "Date", "TenantId", "UserId", "AgentType", "Model",
                 "InputTokens", "OutputTokens", "CostUsd", "ExecutionCount", "UpdatedAt")
            VALUES
                (gen_random_uuid(), CURRENT_DATE, @tenantId, @userId, 'Research', 'anthropic/claude-haiku-4-5-20251001',
                 100, 50, 0.01, 1, now())
            """, connection, transaction);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }
}
