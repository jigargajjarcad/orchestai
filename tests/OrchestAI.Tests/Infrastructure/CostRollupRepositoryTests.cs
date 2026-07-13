using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Task 12 follow-up: proves the query-filter bug fix (UpsertAsync/GetLastRolledUpDateAsync must
// IgnoreQueryFilters() + filter by the row's own TenantId, since BeginSystemWriteScope() leaves the
// ambient TenantId null) and the guard that mirrors CostLedgerRepository.GetDailyAggregatesForRollupAsync.
// See TenantQueryFilterTests.TestDbContextFactory for why this uses a minimal IDbContextFactory
// instead of PooledDbContextFactory<TContext>.
public sealed class CostRollupRepositoryTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string Model = "anthropic/claude-haiku-4-5-20251001";

    private static (CostRollupRepository Repository, IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) Build(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        var factory = new TestDbContextFactory(options, accessor);
        return (new CostRollupRepository(factory, accessor), factory, accessor);
    }

    [Fact]
    public async Task UpsertAsync_CalledTwiceForSameKey_UpdatesExistingRowRatherThanDuplicating()
    {
        var (repository, factory, accessor) = Build(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        using (accessor.BeginSystemWriteScope())
        {
            var first = CostRollup.Create(date, tenantId, UserId, AgentType.Research, Model, 100, 50, 0.01m, 1);
            await repository.UpsertAsync(first, CancellationToken.None);

            // Recomputed values for the same (Date, TenantId, UserId, AgentType, Model) tuple — this
            // is what every subsequent tick's re-roll of an already-rolled-up day looks like.
            var second = CostRollup.Create(date, tenantId, UserId, AgentType.Research, Model, 300, 150, 0.03m, 3);
            await repository.UpsertAsync(second, CancellationToken.None);
        }

        await using var ctx = await factory.CreateDbContextAsync();
        var rows = await ctx.CostRollups.IgnoreQueryFilters().ToListAsync();

        rows.Should().ContainSingle(
            "the second UpsertAsync must UPDATE the row the first call inserted, not insert a duplicate " +
            "that would violate the (Date, TenantId, UserId, AgentType, Model) unique index on the real DB");
        rows[0].InputTokens.Should().Be(300);
        rows[0].OutputTokens.Should().Be(150);
        rows[0].ExecutionCount.Should().Be(3);
    }

    [Fact]
    public async Task UpsertAsync_DifferentTenantsSameUserAgentModelDate_BothPersist()
    {
        var (repository, factory, accessor) = Build(Guid.NewGuid().ToString());
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        // Same UserId (e.g. DatabaseSeeder.EvalSystemUserId), AgentType, Model, and Date across two
        // different tenants — the exact cross-tenant collision the extended unique index
        // ((Date, TenantId, UserId, AgentType, Model), not (Date, UserId, AgentType, Model)) exists
        // to allow.
        using (accessor.BeginSystemWriteScope())
        {
            var rollupA = CostRollup.Create(date, tenantA, UserId, AgentType.Research, Model, 100, 50, 0.01m, 1);
            await repository.UpsertAsync(rollupA, CancellationToken.None);

            var rollupB = CostRollup.Create(date, tenantB, UserId, AgentType.Research, Model, 200, 100, 0.02m, 2);
            await repository.UpsertAsync(rollupB, CancellationToken.None);
        }

        await using var ctx = await factory.CreateDbContextAsync();
        var rows = await ctx.CostRollups.IgnoreQueryFilters().ToListAsync();

        rows.Should().HaveCount(2, "two different tenants' rollups for the same (Date, UserId, AgentType, Model) must not collide");
        rows.Should().Contain(r => r.TenantId == tenantA && r.InputTokens == 100);
        rows.Should().Contain(r => r.TenantId == tenantB && r.InputTokens == 200);
    }

    [Fact]
    public async Task UpsertAsync_SecondUpsertForOneTenant_DoesNotAffectOtherTenantsRow()
    {
        // Guards against a lookup that's scoped by (Date, UserId, AgentType, Model) only (the
        // pre-fix behavior once IgnoreQueryFilters() is added without also filtering by TenantId) —
        // that shape would let tenant A's re-roll silently overwrite tenant B's row instead of
        // inserting/updating its own.
        var (repository, factory, accessor) = Build(Guid.NewGuid().ToString());
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        using (accessor.BeginSystemWriteScope())
        {
            await repository.UpsertAsync(
                CostRollup.Create(date, tenantA, UserId, AgentType.Research, Model, 100, 50, 0.01m, 1), CancellationToken.None);
            await repository.UpsertAsync(
                CostRollup.Create(date, tenantB, UserId, AgentType.Research, Model, 200, 100, 0.02m, 2), CancellationToken.None);

            // Re-roll tenant A's day again with different totals.
            await repository.UpsertAsync(
                CostRollup.Create(date, tenantA, UserId, AgentType.Research, Model, 999, 999, 9.99m, 9), CancellationToken.None);
        }

        await using var ctx = await factory.CreateDbContextAsync();
        var rows = await ctx.CostRollups.IgnoreQueryFilters().ToListAsync();

        rows.Should().HaveCount(2);
        rows.Should().Contain(r => r.TenantId == tenantA && r.InputTokens == 999);
        rows.Should().Contain(r => r.TenantId == tenantB && r.InputTokens == 200, "tenant B's row must be untouched by tenant A's re-roll");
    }

    [Fact]
    public async Task UpsertAsync_OutsideSystemWriteScope_ThrowsInvalidOperationException()
    {
        var (repository, _, _) = Build(Guid.NewGuid().ToString());
        var rollup = CostRollup.Create(
            DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), UserId, AgentType.Research, Model, 1, 1, 0.01m, 1);

        // Deliberately no BeginSystemWriteScope() — matches CostLedgerRepositoryTests' coverage of
        // GetDailyAggregatesForRollupAsync's identical guard.
        var act = async () => await repository.UpsertAsync(rollup, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*system-write scope*");
    }

    [Fact]
    public async Task GetLastRolledUpDateAsync_OutsideSystemWriteScope_ThrowsInvalidOperationException()
    {
        var (repository, _, _) = Build(Guid.NewGuid().ToString());

        var act = async () => await repository.GetLastRolledUpDateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*system-write scope*");
    }

    [Fact]
    public async Task GetLastRolledUpDateAsync_WithinSystemWriteScope_SeesRowsAcrossAllTenants()
    {
        // Proves the fix for bug #1 in the Task 12 follow-up: without IgnoreQueryFilters(), this
        // always returned null (the ambient TenantId is null in this scope, so the normal filter
        // never matches), forcing the background service to re-scan its full MaxCatchUpDays window
        // on every tick instead of advancing past the latest rolled-up date.
        var (repository, factory, accessor) = Build(Guid.NewGuid().ToString());
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var olderDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
        var newerDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        using (accessor.BeginSystemWriteScope())
        {
            await repository.UpsertAsync(
                CostRollup.Create(olderDate, tenantA, UserId, AgentType.Research, Model, 1, 1, 0.01m, 1), CancellationToken.None);
            await repository.UpsertAsync(
                CostRollup.Create(newerDate, tenantB, UserId, AgentType.Research, Model, 1, 1, 0.01m, 1), CancellationToken.None);

            var lastRolledUp = await repository.GetLastRolledUpDateAsync(CancellationToken.None);

            lastRolledUp.Should().Be(newerDate, "the latest date across ALL tenants' rollups, not just one tenant's, must be found");
        }
    }
}
