using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Architecture;

// Two complementary checks (see ADR-014): (1) every entity that DOES implement ITenantScoped
// has an active query filter — proves Task 4's generic reflection-based wiring actually took
// effect. (2) A hand-maintained classification of every entity in the assembly as tenant-scoped
// or globally-shared — catches a FUTURE entity that holds tenant data but forgot to implement
// the interface, which check (1) alone cannot see (it only sees what already opted in).
public sealed class TenantScopingCompletenessTests
{
    // Update this list deliberately whenever a new tenant-owned entity is added — that update IS
    // the point of this test existing.
    private static readonly Type[] ExpectedTenantScopedTypes =
    [
        typeof(OrchestrationTask), typeof(AgentExecution), typeof(AgentMemory), typeof(AgentMessage),
        typeof(AgentRetryAttempt), typeof(CostLedger), typeof(CostRollup), typeof(McpToolCall),
        typeof(TaskCheckpoint), typeof(EvalSuite), typeof(EvalCase), typeof(EvalRun), typeof(EvalResult),
        typeof(RejectionEvent)
    ];

    // Deliberately NOT tenant-scoped — global/shared data (see ADR-014). Listed explicitly so a
    // reviewer of this test sees the full picture, not just the positive list.
    private static readonly Type[] ExpectedGloballySharedTypes =
    [
        typeof(User), typeof(Tenant), typeof(ApiKey), typeof(ModelPricing), typeof(TenantLimits)
    ];

    [Fact]
    public void EveryExpectedType_ImplementsITenantScoped()
    {
        foreach (var type in ExpectedTenantScopedTypes)
        {
            typeof(ITenantScoped).IsAssignableFrom(type).Should().BeTrue(
                $"{type.Name} is expected to be tenant-scoped per the hand-maintained list in this test");
        }
    }

    [Fact]
    public void EveryEntityInTheDomainAssembly_IsAccountedForAsEitherTenantScopedOrGloballyShared()
    {
        var allEntityTypes = typeof(OrchestrationTask).Assembly.GetTypes()
            .Where(t => t.Namespace == "OrchestAI.Domain.Entities" && t.IsClass && !t.IsAbstract)
            .ToList();

        var accountedFor = ExpectedTenantScopedTypes.Concat(ExpectedGloballySharedTypes).ToHashSet();
        var unaccounted = allEntityTypes.Where(t => !accountedFor.Contains(t)).ToList();

        unaccounted.Should().BeEmpty(
            "every entity in OrchestAI.Domain.Entities must be explicitly classified as tenant-scoped or " +
            $"globally-shared in this test — found unclassified: {string.Join(", ", unaccounted.Select(t => t.Name))}");
    }

    [Fact]
    public void EveryITenantScopedEntity_HasAnActiveQueryFilterOnTheRealModel()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var context = new AppDbContext(options, accessor);

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType)) continue;

            entityType.GetQueryFilter().Should().NotBeNull(
                $"{entityType.ClrType.Name} implements ITenantScoped and must have an active query filter (Task 4's generic wiring)");
        }
    }
}
