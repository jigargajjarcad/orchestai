using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Infrastructure.Data.Configurations;

namespace OrchestAI.Infrastructure.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<OrchestrationTask> OrchestrationTasks => Set<OrchestrationTask>();
    public DbSet<AgentExecution> AgentExecutions => Set<AgentExecution>();
    public DbSet<AgentMessage> AgentMessages => Set<AgentMessage>();
    public DbSet<McpToolCall> McpToolCalls => Set<McpToolCall>();
    public DbSet<CostLedger> CostLedger => Set<CostLedger>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new OrchestrationTaskConfiguration());
        modelBuilder.ApplyConfiguration(new AgentExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new AgentMessageConfiguration());
        modelBuilder.ApplyConfiguration(new McpToolCallConfiguration());
        modelBuilder.ApplyConfiguration(new CostLedgerConfiguration());
    }
}
