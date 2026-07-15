using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data.Configurations;

namespace OrchestAI.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    private readonly ICurrentTenantAccessor _tenantAccessor;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentTenantAccessor tenantAccessor)
        : base(options)
    {
        _tenantAccessor = tenantAccessor;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<OrchestrationTask> OrchestrationTasks => Set<OrchestrationTask>();
    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
    public DbSet<AgentMessage> AgentMessages => Set<AgentMessage>();
    public DbSet<McpToolCall> McpToolCalls => Set<McpToolCall>();
    public DbSet<CostLedger> CostLedger => Set<CostLedger>();
    public DbSet<TaskCheckpoint> TaskCheckpoints => Set<TaskCheckpoint>();
    public DbSet<AgentMemory> AgentMemories => Set<AgentMemory>();
    public DbSet<AgentRetryAttempt> AgentRetryAttempts => Set<AgentRetryAttempt>();
    public DbSet<CostRollup> CostRollups => Set<CostRollup>();
    public DbSet<ModelPricing> ModelPricing => Set<ModelPricing>();
    public DbSet<TenantLimits> TenantLimits => Set<TenantLimits>();
    public DbSet<EvalSuite> EvalSuites => Set<EvalSuite>();
    public DbSet<EvalCase> EvalCases => Set<EvalCase>();
    public DbSet<EvalRun> EvalRuns => Set<EvalRun>();
    public DbSet<EvalResult> EvalResults => Set<EvalResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new ApiKeyConfiguration());
        modelBuilder.ApplyConfiguration(new OrchestrationTaskConfiguration());
        modelBuilder.ApplyConfiguration(new AgentExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new AgentMessageConfiguration());
        modelBuilder.ApplyConfiguration(new McpToolCallConfiguration());
        modelBuilder.ApplyConfiguration(new CostLedgerConfiguration());
        modelBuilder.ApplyConfiguration(new TaskCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new AgentMemoryConfiguration());
        modelBuilder.ApplyConfiguration(new AgentRetryAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new CostRollupConfiguration());
        modelBuilder.ApplyConfiguration(new ModelPricingConfiguration());
        modelBuilder.ApplyConfiguration(new TenantLimitsConfiguration());
        modelBuilder.ApplyConfiguration(new EvalSuiteConfiguration());
        modelBuilder.ApplyConfiguration(new EvalCaseConfiguration());
        modelBuilder.ApplyConfiguration(new EvalRunConfiguration());
        modelBuilder.ApplyConfiguration(new EvalResultConfiguration());

        ApplyTenantQueryFilters(modelBuilder);
    }

    // Applies the SAME filter shape to every entity implementing ITenantScoped, generically —
    // so a future entity that implements the interface is protected automatically, with no new
    // HasQueryFilter call to remember. See ADR-014 and TenantQueryFilterTests for the fail-closed
    // proof (comparing against a null ambient TenantId returns zero rows, never all rows).
    private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
    {
        var buildFilterMethod = typeof(AppDbContext).GetMethod(
            nameof(BuildTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType)) continue;

            var typedMethod = buildFilterMethod.MakeGenericMethod(entityType.ClrType);
            var filter = (LambdaExpression)typedMethod.Invoke(this, null)!;
            entityType.SetQueryFilter(filter);
        }
    }

    private LambdaExpression BuildTenantFilter<TEntity>() where TEntity : class, ITenantScoped
    {
        Expression<Func<TEntity, bool>> filter = e => e.TenantId == _tenantAccessor.TenantId;
        return filter;
    }
}
