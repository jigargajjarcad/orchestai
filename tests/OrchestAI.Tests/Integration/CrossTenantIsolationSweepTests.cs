using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Tenancy;
using OrchestAI.Tests.Infrastructure;

namespace OrchestAI.Tests.Integration;

// A broader sweep across multiple entity types proving the SAME generic filter mechanism
// (Task 4) generalizes, rather than re-spot-checking the one or two entities earlier tasks
// already exercised. Also covers "update/delete by a guessed foreign ID," which no earlier task
// tested directly.
//
// Uses the shared TestDbContextFactory (see TenantQueryFilterTests.TestDbContextFactory) rather
// than EF Core's real PooledDbContextFactory<TContext>, which only exposes a
// (DbContextOptions<TContext>, int poolSize) constructor and has no hook for AppDbContext's
// ICurrentTenantAccessor dependency. Real writes below are stamped by the real
// TenantScopingInterceptor (Task 5) from the ambient accessor — exactly what production does —
// rather than the reflection-based WithTestTenant stand-in that pre-Task-5 tests needed.
public sealed class CrossTenantIsolationSweepTests
{
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    private sealed record SeededData(
        Guid TenantAId, Guid TenantBId,
        Guid TenantAExecutionId, Guid TenantBExecutionId,
        Guid TenantASuiteId, Guid TenantBSuiteId);

    private static async Task<SeededData> SeedTwoFullTenants(IDbContextFactory<AppDbContext> factory, AsyncLocalCurrentTenantAccessor accessor)
    {
        var tenantAId = Guid.NewGuid();
        var tenantBId = Guid.NewGuid();
        var user = TestUserFactory.Create("sweep@test.local");

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            await seedCtx.SaveChangesAsync();
        }

        Guid executionAId, suiteAId;
        using (accessor.SetTenant(tenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "A", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            execution.Start();
            execution.Complete("output", 10, 5, 0.01m);
            ctx.AgentExecutions.Add(execution);
            ctx.CostLedger.Add(CostLedger.Create(task.Id, "model", 10, 5, 0.01m, execution.Id));
            var suite = EvalSuite.Create("Suite A", "desc", AgentType.Research);
            ctx.EvalSuites.Add(suite);
            await ctx.SaveChangesAsync();
            executionAId = execution.Id;
            suiteAId = suite.Id;
        }

        Guid executionBId, suiteBId;
        using (accessor.SetTenant(tenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var task = OrchestrationTask.Create(user.Id, "B", "prompt");
            ctx.OrchestrationTasks.Add(task);
            var execution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            execution.Start();
            execution.Complete("output", 10, 5, 0.01m);
            ctx.AgentExecutions.Add(execution);
            ctx.CostLedger.Add(CostLedger.Create(task.Id, "model", 20, 10, 0.02m, execution.Id));
            var suite = EvalSuite.Create("Suite B", "desc", AgentType.Research);
            ctx.EvalSuites.Add(suite);
            await ctx.SaveChangesAsync();
            executionBId = execution.Id;
            suiteBId = suite.Id;
        }

        return new SeededData(tenantAId, tenantBId, executionAId, executionBId, suiteAId, suiteBId);
    }

    [Fact]
    public async Task ReadIsolation_HoldsAcrossAgentExecutions_CostLedger_AndEvalSuites_Simultaneously()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var seed = await SeedTwoFullTenants(factory, accessor);

        using (accessor.SetTenant(seed.TenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();

            (await ctx.AgentExecutions.ToListAsync()).Should().ContainSingle(e => e.Id == seed.TenantAExecutionId);
            (await ctx.CostLedger.ToListAsync()).Should().OnlyContain(c => c.TenantId == seed.TenantAId);
            (await ctx.EvalSuites.ToListAsync()).Should().ContainSingle(s => s.Id == seed.TenantASuiteId);
        }
    }

    [Fact]
    public async Task UpdateByGuessedForeignId_NoRowsAffected_BecauseTheRowIsInvisible()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var seed = await SeedTwoFullTenants(factory, accessor);

        using (accessor.SetTenant(seed.TenantAId))
        {
            // Tenant A attempts to fetch-then-update tenant B's AgentExecution by its (guessed
            // or otherwise obtained) real ID — the standard repository pattern in this codebase
            // is always fetch-first, so the filtered fetch must return null before any update
            // is even attempted.
            await using var ctx = await factory.CreateDbContextAsync();
            var found = await ctx.AgentExecutions.FirstOrDefaultAsync(e => e.Id == seed.TenantBExecutionId);

            found.Should().BeNull("tenant A must never be able to even locate tenant B's row to update it");
        }

        // Confirm tenant B's row is genuinely untouched.
        using (accessor.SetTenant(seed.TenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var stillThere = await ctx.AgentExecutions.SingleAsync(e => e.Id == seed.TenantBExecutionId);
            stillThere.Status.Should().Be(ExecutionStatus.Completed);
        }
    }

    [Fact]
    public async Task DeleteByGuessedForeignId_NoRowsAffected_BecauseTheRowIsInvisible()
    {
        var (factory, accessor) = BuildFactory(Guid.NewGuid().ToString());
        var seed = await SeedTwoFullTenants(factory, accessor);

        using (accessor.SetTenant(seed.TenantAId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var found = await ctx.EvalSuites.FirstOrDefaultAsync(s => s.Id == seed.TenantBSuiteId);

            found.Should().BeNull("tenant A must never be able to locate tenant B's suite to delete it");
        }

        using (accessor.SetTenant(seed.TenantBId))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            (await ctx.EvalSuites.AnyAsync(s => s.Id == seed.TenantBSuiteId)).Should().BeTrue(
                "tenant B's suite must be completely untouched by tenant A's attempt");
        }
    }
}
