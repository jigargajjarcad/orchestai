using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IModelPricingRepository
{
    Task<ModelPricing?> GetByModelAsync(string model, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ModelPricing>> GetAllAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(ModelPricing pricing, CancellationToken cancellationToken = default);
}
