using FluentAssertions;
using OrchestAI.Infrastructure.Agents;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AsyncLocalTaskToolCallBudgetTests
{
    [Fact]
    public void TryIncrement_NoScopeOpen_AlwaysAllowed()
    {
        var budget = new AsyncLocalTaskToolCallBudget();

        budget.TryIncrement().Allowed.Should().BeTrue();
    }

    [Fact]
    public void TryIncrement_WithinCap_Allowed()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using var scope = budget.BeginScope(maxToolCalls: 3);

        budget.TryIncrement().Allowed.Should().BeTrue();
        budget.TryIncrement().Allowed.Should().BeTrue();
        budget.TryIncrement().Allowed.Should().BeTrue();
    }

    [Fact]
    public void TryIncrement_ExceedingCap_NotAllowed()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using var scope = budget.BeginScope(maxToolCalls: 2);

        budget.TryIncrement();
        budget.TryIncrement();
        var thirdCheck = budget.TryIncrement();

        thirdCheck.Allowed.Should().BeFalse();
        thirdCheck.MaxToolCalls.Should().Be(2);
        thirdCheck.CurrentCount.Should().Be(3);
    }

    [Fact]
    public void Dispose_RestoresPreviousScope()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using (budget.BeginScope(maxToolCalls: 1))
        {
            using (budget.BeginScope(maxToolCalls: 100))
            {
                budget.TryIncrement().Allowed.Should().BeTrue();
            }

            // Back in the outer (cap-of-1) scope — the inner scope's consumed calls must not
            // have touched the outer counter.
            budget.TryIncrement().Allowed.Should().BeTrue();
            budget.TryIncrement().Allowed.Should().BeFalse();
        }
    }

    [Fact]
    public async Task TryIncrement_ConcurrentIncrementsWithinOneScope_NeverExceedsCapAcrossParallelCallers()
    {
        var budget = new AsyncLocalTaskToolCallBudget();
        using var scope = budget.BeginScope(maxToolCalls: 50);

        // Mirrors how parallel sub-agents (Task.WhenAll in StartOrchestrationHandler) each fork
        // from the same ambient scope and increment the SAME shared counter concurrently.
        var tasks = Enumerable.Range(0, 200).Select(_ => Task.Run(() => budget.TryIncrement().Allowed)).ToArray();
        var results = await Task.WhenAll(tasks);

        results.Count(allowed => allowed).Should().Be(50,
            "exactly the cap's worth of concurrent increments should succeed, never more");
    }
}
