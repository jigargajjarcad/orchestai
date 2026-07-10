using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalResultRepositoryIdempotencyTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    [Fact]
    public async Task GetScoredAgentExecutionIdsAsync_ReturnsOnlyMatchesForExactScorerAndVersion()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
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
        var factory = BuildFactory(dbName);
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
        var factory = BuildFactory(dbName);
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
        var factory = BuildFactory(dbName);
        var repository = new EvalResultRepository(factory);

        var act = async () => await repository.DeletePostHocResultAsync(
            Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
