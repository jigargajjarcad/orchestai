using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface ICostLedgerRepository
{
    Task AddAsync(CostLedger ledger, CancellationToken cancellationToken = default);
}
