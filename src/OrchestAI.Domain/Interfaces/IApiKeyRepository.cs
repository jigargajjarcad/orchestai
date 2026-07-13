using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IApiKeyRepository
{
    Task<ApiKey?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ApiKey?> GetByPublicKeyIdAsync(string publicKeyId, CancellationToken cancellationToken = default);
    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
    Task UpdateAsync(ApiKey apiKey, CancellationToken cancellationToken = default);
}
