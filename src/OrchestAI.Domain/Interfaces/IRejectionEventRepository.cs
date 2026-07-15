using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IRejectionEventRepository
{
    Task AddAsync(RejectionEvent rejectionEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RejectionEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
}
