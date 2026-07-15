using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IIdempotencyRecordRepository
{
    // Returns null for a missing OR expired record — an expired key is functionally absent,
    // free to be reused for a brand-new task.
    Task<IdempotencyRecord?> GetByKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default);

    Task AddAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
}
