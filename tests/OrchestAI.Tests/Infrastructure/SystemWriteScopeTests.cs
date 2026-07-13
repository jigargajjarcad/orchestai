using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class SystemWriteScopeTests
{
    // PooledDbContextFactory<AppDbContext> cannot be used here: its only public constructor is
    // (DbContextOptions<TContext>, int poolSize) with no hook for AppDbContext's second
    // constructor parameter (ICurrentTenantAccessor). Every other test file in this codebase
    // hits the same wall (see TenantQueryFilterTests.TestDbContextFactory) and uses the shared
    // minimal IDbContextFactory<AppDbContext> instead — do the same here for consistency.
    private static IDbContextFactory<AppDbContext> BuildFactory(string dbName, AsyncLocalCurrentTenantAccessor accessor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return new TestDbContextFactory(options, accessor);
    }

    [Fact]
    public void IsSystemWriteScope_DefaultsToFalse()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();

        accessor.IsSystemWriteScope.Should().BeFalse();
    }

    [Fact]
    public void BeginSystemWriteScope_SetsFlagAndRestoresOnDispose()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();

        using (accessor.BeginSystemWriteScope())
        {
            accessor.IsSystemWriteScope.Should().BeTrue();
        }

        accessor.IsSystemWriteScope.Should().BeFalse();
    }

    [Fact]
    public async Task SaveChanges_InSystemWriteScope_AllowsMultipleDistinctTenantIdsInOneBatch()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = BuildFactory(Guid.NewGuid().ToString(), accessor);
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();

        using (accessor.BeginSystemWriteScope())
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var rollupA = CostRollup.Create(DateOnly.FromDateTime(DateTime.UtcNow), tenantAId, Guid.NewGuid(), AgentType.Research, "model", 10, 5, 0.01m, 1);
            var rollupB = CostRollup.Create(DateOnly.FromDateTime(DateTime.UtcNow), tenantBId, Guid.NewGuid(), AgentType.Research, "model", 20, 10, 0.02m, 1);
            ctx.CostRollups.AddRange(rollupA, rollupB);

            var act = async () => await ctx.SaveChangesAsync();

            await act.Should().NotThrowAsync("the system-write scope must allow a single batch to persist rows for multiple different tenants");
        }
    }

    [Fact]
    public async Task SaveChanges_OutsideSystemWriteScope_StillEnforcesNormalTenantRulesForCostRollup()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var factory = BuildFactory(Guid.NewGuid().ToString(), accessor);

        // Deliberately no system-write scope and no tenant scope — CostRollup is still
        // ITenantScoped, so ordinary (non-system) code must not be able to bypass enforcement
        // just because CostRollup happens to also support the system path.
        await using var ctx = await factory.CreateDbContextAsync();
        var rollup = CostRollup.Create(DateOnly.FromDateTime(DateTime.UtcNow), Guid.NewGuid(), Guid.NewGuid(), AgentType.Research, "model", 10, 5, 0.01m, 1);
        ctx.CostRollups.Add(rollup);

        var act = async () => await ctx.SaveChangesAsync();

        await act.Should().ThrowAsync<OrchestAI.Application.Exceptions.TenantContextViolationException>();
    }
}
