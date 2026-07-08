using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IModelPricingCache
{
    Task<ModelPricing?> GetAsync(string model, CancellationToken cancellationToken = default);
}
