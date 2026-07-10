using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AgentExecutionRepositoryPostHocSelectionTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    [Fact]
    public async Task SelectForPostHocScoringAsync_FiltersByDateRangeAgentTypeAndCompletedStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var user = TestUserFactory.Create("posthoc-select@test.local");
        var task = OrchestrationTask.Create(user.Id, "t", "prompt");

        var inRangeResearch = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        inRangeResearch.Start();
        inRangeResearch.Complete("output", 10, 5, 0.01m);

        var inRangeCode = AgentExecution.Create(task.Id, AgentType.Code, "prompt");
        inRangeCode.Start();
        inRangeCode.Complete("output", 10, 5, 0.01m);

        var stillRunning = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        stillRunning.Start();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(inRangeResearch, inRangeCode, stillRunning);
            await seedCtx.SaveChangesAsync();
        }

        var repository = new AgentExecutionRepository(factory);
        var result = await repository.SelectForPostHocScoringAsync(
            from: DateTimeOffset.UtcNow.AddDays(-1), to: DateTimeOffset.UtcNow.AddDays(1),
            agentType: AgentType.Research, explicitTraceIds: null, limit: 10,
            cancellationToken: CancellationToken.None);

        result.TotalMatched.Should().Be(1);
        result.AgentExecutionIds.Should().ContainSingle().Which.Should().Be(inRangeResearch.Id);
    }

    [Fact]
    public async Task SelectForPostHocScoringAsync_TotalMatchedExceedsLimit_ReturnsFullCountWithTruncatedIds()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var user = TestUserFactory.Create("posthoc-cap@test.local");
        var task = OrchestrationTask.Create(user.Id, "t", "prompt");

        var executions = Enumerable.Range(0, 5).Select(_ =>
        {
            var e = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            e.Start();
            e.Complete("output", 10, 5, 0.01m);
            return e;
        }).ToList();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(executions);
            await seedCtx.SaveChangesAsync();
        }

        var repository = new AgentExecutionRepository(factory);
        var result = await repository.SelectForPostHocScoringAsync(
            from: DateTimeOffset.UtcNow.AddDays(-1), to: DateTimeOffset.UtcNow.AddDays(1),
            agentType: null, explicitTraceIds: null, limit: 3, cancellationToken: CancellationToken.None);

        result.TotalMatched.Should().Be(5);
        result.AgentExecutionIds.Should().HaveCount(3);
    }

    [Fact]
    public async Task SelectForPostHocScoringAsync_ExplicitTraceIds_IgnoresDateRangeAndUsesIdsOnly()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var user = TestUserFactory.Create("posthoc-explicit@test.local");
        var task = OrchestrationTask.Create(user.Id, "t", "prompt");

        var wanted = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        wanted.Start();
        wanted.Complete("output", 10, 5, 0.01m);

        var notWanted = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        notWanted.Start();
        notWanted.Complete("output", 10, 5, 0.01m);

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(wanted, notWanted);
            await seedCtx.SaveChangesAsync();
        }

        var repository = new AgentExecutionRepository(factory);
        var result = await repository.SelectForPostHocScoringAsync(
            from: null, to: null, agentType: null, explicitTraceIds: [wanted.Id], limit: 10,
            cancellationToken: CancellationToken.None);

        result.TotalMatched.Should().Be(1);
        result.AgentExecutionIds.Should().ContainSingle().Which.Should().Be(wanted.Id);
    }
}
