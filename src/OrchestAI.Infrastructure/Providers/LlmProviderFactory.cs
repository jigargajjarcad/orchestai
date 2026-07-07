using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Providers;

public sealed class LlmProviderFactory : ILlmProviderFactory
{
    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;

    public LlmProviderFactory(IEnumerable<ILlmProvider> providers)
        => _providers = providers.ToDictionary(p => p.ProviderId, StringComparer.OrdinalIgnoreCase);

    public ILlmProvider Resolve(string providerId)
        => _providers.TryGetValue(providerId, out var provider)
            ? provider
            : throw new InvalidOperationException(
                $"No LLM provider registered for '{providerId}'. Configured providers: {string.Join(", ", _providers.Keys)}");
}
