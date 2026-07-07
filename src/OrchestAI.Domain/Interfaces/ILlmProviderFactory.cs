namespace OrchestAI.Domain.Interfaces;

public interface ILlmProviderFactory
{
    ILlmProvider Resolve(string providerId);
}
