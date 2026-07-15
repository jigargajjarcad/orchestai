namespace OrchestAI.Domain.Entities;

// One row per tenant, created only via SetTenantLimitsHandler (admin/bootstrap-only — see
// AdminController). Every field is nullable: null means "use TenantLimitsDefaults," never
// "use zero" or "unlimited." Read exclusively through ITenantLimitsProvider — see
// DESIGN_PRINCIPLES.md "Single-choke-point enforcement" and ADR-015.
//
// Not ITenantScoped — like Tenant/ApiKey, this is an identity/config table the admin caller
// (no ambient tenant) addresses by an explicit TenantId column, not the global query filter.
public sealed class TenantLimits
{
    private TenantLimits() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public int? RequestsPerMinute { get; private set; }
    public int? MaxConcurrentTasks { get; private set; }
    public int? MaxAgentsPerTask { get; private set; }
    public int? MaxToolCallsPerTask { get; private set; }
    public decimal? DailyCostBudgetUsd { get; private set; }
    public decimal? MonthlyCostBudgetUsd { get; private set; }
    public int? MaxQueueDepth { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    // Third named exception to ADR-014's "no ITenantScoped factory takes TenantId" rule
    // (ApiKey.Create/CostRollup.Create were the first two) — same shape: reachable only
    // through the admin-secret-gated AdminController, no ambient tenant to bypass. See ADR-015.
    public static TenantLimits Create(
        Guid tenantId,
        int? requestsPerMinute,
        int? maxConcurrentTasks,
        int? maxAgentsPerTask,
        int? maxToolCallsPerTask,
        decimal? dailyCostBudgetUsd,
        decimal? monthlyCostBudgetUsd,
        int? maxQueueDepth)
    {
        return new TenantLimits
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestsPerMinute = requestsPerMinute,
            MaxConcurrentTasks = maxConcurrentTasks,
            MaxAgentsPerTask = maxAgentsPerTask,
            MaxToolCallsPerTask = maxToolCallsPerTask,
            DailyCostBudgetUsd = dailyCostBudgetUsd,
            MonthlyCostBudgetUsd = monthlyCostBudgetUsd,
            MaxQueueDepth = maxQueueDepth,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Update(
        int? requestsPerMinute,
        int? maxConcurrentTasks,
        int? maxAgentsPerTask,
        int? maxToolCallsPerTask,
        decimal? dailyCostBudgetUsd,
        decimal? monthlyCostBudgetUsd,
        int? maxQueueDepth)
    {
        RequestsPerMinute = requestsPerMinute;
        MaxConcurrentTasks = maxConcurrentTasks;
        MaxAgentsPerTask = maxAgentsPerTask;
        MaxToolCallsPerTask = maxToolCallsPerTask;
        DailyCostBudgetUsd = dailyCostBudgetUsd;
        MonthlyCostBudgetUsd = monthlyCostBudgetUsd;
        MaxQueueDepth = maxQueueDepth;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
