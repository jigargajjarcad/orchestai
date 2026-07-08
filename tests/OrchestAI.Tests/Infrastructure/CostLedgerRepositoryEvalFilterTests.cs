using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;

namespace OrchestAI.Tests.Infrastructure;

public sealed class CostLedgerRepositoryEvalFilterTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    [Fact]
    public async Task GetDailyAggregatesAsync_MixOfProductionAndEvalRows_OnlyReturnsProduction()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Guid userId;

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            var user = TestUserFactory.Create("cost-filter@test.local");
            userId = user.Id;
            var task = OrchestrationTask.Create(user.Id, "t", "prompt");
            var prodExecution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            var evalExecution = AgentExecution.Create(task.Id, AgentType.Research, "prompt", evalRunId: Guid.NewGuid());

            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(prodExecution, evalExecution);
            seedCtx.CostLedger.AddRange(
                CostLedger.Create(task.Id, "anthropic/model", 100, 50, 0.01m, prodExecution.Id),
                CostLedger.Create(
                    task.Id, "anthropic/model", 999, 999, 9.99m, evalExecution.Id,
                    source: CostSource.Eval, evalRunId: evalExecution.EvalRunId));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new CostLedgerRepository(factory);
        var aggregates = await repository.GetDailyAggregatesAsync(today, today, CancellationToken.None);

        aggregates.Should().ContainSingle();
        aggregates[0].UserId.Should().Be(userId);
        aggregates[0].InputTokens.Should().Be(100);
        aggregates[0].CostUsd.Should().Be(0.01m);
    }
}

internal static class TestUserFactory
{
    public static User Create(string email)
    {
        var user = (User)Activator.CreateInstance(typeof(User), nonPublic: true)!;
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, Guid.NewGuid());
        typeof(User).GetProperty(nameof(User.Email))!.SetValue(user, email);
        typeof(User).GetProperty(nameof(User.DisplayName))!.SetValue(user, "Test User");
        typeof(User).GetProperty(nameof(User.CreatedAt))!.SetValue(user, DateTimeOffset.UtcNow);
        typeof(User).GetProperty(nameof(User.UpdatedAt))!.SetValue(user, DateTimeOffset.UtcNow);
        return user;
    }
}
