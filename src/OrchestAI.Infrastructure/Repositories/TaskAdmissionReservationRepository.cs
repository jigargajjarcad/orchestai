using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class TaskAdmissionReservationRepository : ITaskAdmissionReservationRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public TaskAdmissionReservationRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<TaskAdmissionReservation?> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.TaskAdmissionReservations
            .FirstOrDefaultAsync(x => x.TaskId == taskId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReleaseAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.TaskAdmissionReservations
            .Where(x => x.TaskId == taskId)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
