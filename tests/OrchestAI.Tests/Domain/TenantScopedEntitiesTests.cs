using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Domain;

// Proves TenantId is never settable via any public factory — only TenantScopingInterceptor
// (Task 5) writes it, via reflection exactly like UpdatedAtInterceptor stamps UpdatedAt. Every
// entity here must implement ITenantScoped and default to Guid.Empty until stamped.
public sealed class TenantScopedEntitiesTests
{
    [Fact]
    public void OrchestrationTask_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var task = OrchestrationTask.Create(Guid.NewGuid(), "title", "prompt");
        (task as ITenantScoped).Should().NotBeNull();
        task.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentExecution_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var execution = AgentExecution.Create(Guid.NewGuid(), AgentType.Research, "prompt");
        (execution as ITenantScoped).Should().NotBeNull();
        execution.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentMemory_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var memory = AgentMemory.Create(Guid.NewGuid(), AgentType.Research, "key", "value");
        (memory as ITenantScoped).Should().NotBeNull();
        memory.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentMessage_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var message = AgentMessage.Create(Guid.NewGuid(), MessageRole.Assistant, "content", 0);
        (message as ITenantScoped).Should().NotBeNull();
        message.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void AgentRetryAttempt_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var retry = AgentRetryAttempt.Create(Guid.NewGuid(), 1, 500, "timeout");
        (retry as ITenantScoped).Should().NotBeNull();
        retry.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void CostLedger_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var ledger = CostLedger.Create(Guid.NewGuid(), "model", 10, 5, 0.01m);
        (ledger as ITenantScoped).Should().NotBeNull();
        ledger.TenantId.Should().Be(Guid.Empty);
    }

    // CostRollup is deliberately excluded from this class: its Create(...) is one of exactly two
    // named exceptions (alongside ApiKey.Create, also excluded here — see ApiKeyTests.cs) to
    // "TenantId is never settable via any public factory." CostRollup.Create takes an explicit
    // tenantId parameter by design (Task 12 / ADR-014 confirmation #5b), so it does not default
    // to Guid.Empty the way every other entity in this file does.

    [Fact]
    public void McpToolCall_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var call = McpToolCall.Create(Guid.NewGuid(), "tool", "{}", "parent-span");
        (call as ITenantScoped).Should().NotBeNull();
        call.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TaskCheckpoint_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var checkpoint = TaskCheckpoint.Create(Guid.NewGuid(), AgentType.Research, Guid.NewGuid(), "output", 10, 5, 0.01m);
        (checkpoint as ITenantScoped).Should().NotBeNull();
        checkpoint.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalSuite_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var suite = EvalSuite.Create("suite", "desc", AgentType.Research);
        (suite as ITenantScoped).Should().NotBeNull();
        suite.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalCase_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var evalCase = EvalCase.Create(Guid.NewGuid(), "{}", "{}", EvalScorerType.RuleBased, 0.1m);
        (evalCase as ITenantScoped).Should().NotBeNull();
        evalCase.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalRun_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "v1", null);
        (run as ITenantScoped).Should().NotBeNull();
        run.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void EvalResult_ImplementsITenantScoped_DefaultsToEmpty()
    {
        var result = EvalResult.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvalScorerType.RuleBased, "v1", 1.0m, true, "{}");
        (result as ITenantScoped).Should().NotBeNull();
        result.TenantId.Should().Be(Guid.Empty);
    }
}
