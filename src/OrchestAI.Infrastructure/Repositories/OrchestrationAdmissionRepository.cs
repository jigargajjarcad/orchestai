using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

// This codebase's first explicit multi-statement DB transaction — see Task 6's investigation
// note for why the all-or-nothing guarantee requires it. Cannot be tested against EF Core's
// InMemory provider at all (neither ExecuteUpdateAsync nor FOR UPDATE translate) — see
// OrchestrationAdmissionRepositoryTests, which runs against real Postgres.
public sealed class OrchestrationAdmissionRepository : IOrchestrationAdmissionRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public OrchestrationAdmissionRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<AdmissionResult> TryAdmitAsync(
        Guid taskId,
        Guid tenantId,
        int maxConcurrentTasks,
        decimal reservationAmountUsd,
        decimal dailyBudgetUsd,
        decimal actualDailySpendUsd,
        decimal monthlyBudgetUsd,
        decimal actualMonthlySpendUsd,
        TimeSpan reservationStaleness,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await ctx.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Serializes concurrent admissions for THIS tenant only — a different tenant's admission
        // locks its own Tenants row and never blocks on this one. Tenant is not ITenantScoped,
        // so this query is never affected by the tenant global filter either way.
        await ctx.Tenants
            .FromSqlInterpolated($"SELECT * FROM \"Tenants\" WHERE \"Id\" = {tenantId} FOR UPDATE")
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // The atomic Pending -> Running CAS. OrchestrationTask IS ITenantScoped, so the global
        // query filter is automatically ANDed into this WHERE clause too — a taskId belonging to
        // a different tenant than the caller's ambient scope would find 0 rows here, failing
        // closed, not just failing on a later explicit check.
        var rowsUpdated = await ctx.OrchestrationTasks
            .Where(t => t.Id == taskId && t.Status == OrchestrationTaskStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, OrchestrationTaskStatus.Running), cancellationToken)
            .ConfigureAwait(false);

        if (rowsUpdated == 0)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return new AdmissionResult(false, AdmissionFailureReason.TaskNotPending, null);
        }

        var staleThreshold = DateTimeOffset.UtcNow - reservationStaleness;

        var activeCount = await ctx.TaskAdmissionReservations
            .Where(r => r.TenantId == tenantId && r.CreatedAt > staleThreshold)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeCount >= maxConcurrentTasks)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var details = $$"""{"limit":{{maxConcurrentTasks}},"actual":{{activeCount}}}""";
            return new AdmissionResult(false, AdmissionFailureReason.ConcurrencyExceeded, details);
        }

        var activeReservedUsd = await ctx.TaskAdmissionReservations
            .Where(r => r.TenantId == tenantId && r.CreatedAt > staleThreshold)
            .SumAsync(r => (decimal?)r.ReservedCostUsd, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        var projectedDaily = actualDailySpendUsd + activeReservedUsd + reservationAmountUsd;
        if (projectedDaily > dailyBudgetUsd)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var details = $$"""{"limit":{{dailyBudgetUsd}},"actual":{{projectedDaily}},"period":"day"}""";
            return new AdmissionResult(false, AdmissionFailureReason.BudgetExceeded, details);
        }

        var projectedMonthly = actualMonthlySpendUsd + activeReservedUsd + reservationAmountUsd;
        if (projectedMonthly > monthlyBudgetUsd)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var details = $$"""{"limit":{{monthlyBudgetUsd}},"actual":{{projectedMonthly}},"period":"month"}""";
            return new AdmissionResult(false, AdmissionFailureReason.BudgetExceeded, details);
        }

        var reservation = TaskAdmissionReservation.Create(taskId, reservationAmountUsd);
        ctx.TaskAdmissionReservations.Add(reservation);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AdmissionResult(true, null, null);
    }
}
