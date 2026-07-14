using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalResultRepositoryIdempotencyTests
{
    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) BuildFactory(string dbName)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        // See TenantQueryFilterTests.TestDbContextFactory: PooledDbContextFactory<TContext> has
        // no hook for AppDbContext's new ICurrentTenantAccessor dependency, so this test uses the
        // shared minimal IDbContextFactory<AppDbContext> instead.
        return (new TestDbContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task GetScoredAgentExecutionIdsAsync_ReturnsOnlyMatchesForExactScorerAndVersion()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);
        // This test predates tenant scoping (Task 10) and TenantScopingInterceptor (Task 5, not
        // yet on this branch) — seeded EvalResult rows keep their Guid.Empty TenantId default.
        // Scoping the ambient tenant to Guid.Empty makes Task 4's new query filter transparent.
        using var tenantScope = accessor.SetTenant(Guid.Empty);
        var runId = Guid.NewGuid();
        var scoredExecutionId = Guid.NewGuid();
        var unscoredExecutionId = Guid.NewGuid();
        var differentVersionExecutionId = Guid.NewGuid();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.EvalResults.AddRange(
                EvalResult.Create(runId, null, scoredExecutionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"),
                EvalResult.Create(runId, null, differentVersionExecutionId, EvalScorerType.LlmJudge, "llm-judge-v0", 0.9m, true, "{}"));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new EvalResultRepository(factory);
        var scored = await repository.GetScoredAgentExecutionIdsAsync(
            [scoredExecutionId, unscoredExecutionId, differentVersionExecutionId],
            EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        scored.Should().ContainSingle().Which.Should().Be(scoredExecutionId);
    }

    [Fact]
    public async Task GetScoredAgentExecutionIdsAsync_LiveCaseBasedResult_NeverCounted()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);
        using var tenantScope = accessor.SetTenant(Guid.Empty);
        var executionId = Guid.NewGuid();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            // A live-suite result for the same execution/scorer/version, but with a real EvalCaseId —
            // must not be mistaken for a prior post-hoc score of this trace.
            seedCtx.EvalResults.Add(
                EvalResult.Create(Guid.NewGuid(), Guid.NewGuid(), executionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new EvalResultRepository(factory);
        var scored = await repository.GetScoredAgentExecutionIdsAsync(
            [executionId], EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        scored.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePostHocResultAsync_PriorResultExists_RemovesItAndAllowsReinsert()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, accessor) = BuildFactory(dbName);
        using var tenantScope = accessor.SetTenant(Guid.Empty);
        var executionId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.EvalResults.Add(
                EvalResult.Create(runId, null, executionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.3m, false, "{}"));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new EvalResultRepository(factory);
        await repository.DeletePostHocResultAsync(executionId, EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        var stillScored = await repository.GetScoredAgentExecutionIdsAsync(
            [executionId], EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);
        stillScored.Should().BeEmpty("the prior post-hoc result was deleted so re-scoring the same trace can proceed");

        // Re-insert (simulating the supersede: delete-then-add) must not violate the partial unique index.
        await repository.AddAsync(
            EvalResult.Create(runId, null, executionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"),
            CancellationToken.None);

        var results = await repository.GetByRunIdAsync(runId, CancellationToken.None);
        results.Should().ContainSingle().Which.Score.Should().Be(0.9m);
    }

    [Fact]
    public async Task DeletePostHocResultAsync_NoPriorResult_IsNoOp()
    {
        var dbName = Guid.NewGuid().ToString();
        var (factory, _) = BuildFactory(dbName);
        var repository = new EvalResultRepository(factory);

        var act = async () => await repository.DeletePostHocResultAsync(
            Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
