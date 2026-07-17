namespace OrchestAI.Domain.Interfaces;

using OrchestAI.Domain.Models;

public interface IReadinessChecker
{
    Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken);
}
