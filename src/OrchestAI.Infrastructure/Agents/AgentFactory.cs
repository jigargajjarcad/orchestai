using Microsoft.Extensions.DependencyInjection;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Agents;

public sealed class AgentFactory : IAgentFactory
{
    private readonly IServiceProvider _serviceProvider;

    public AgentFactory(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    public IAgent Create(AgentType agentType) => agentType switch
    {
        AgentType.Research => GetRequired<ResearchAgent>(),
        AgentType.Writer => GetRequired<WriterAgent>(),
        AgentType.Code => GetRequired<CodeAgent>(),
        AgentType.Data => GetRequired<DataAgent>(),
        AgentType.Browser => GetRequired<BrowserAgent>(),
        _ => throw new ArgumentOutOfRangeException(nameof(agentType), agentType, "Unknown agent type")
    };

    private T GetRequired<T>() where T : notnull
        => (T)_serviceProvider.GetRequiredService(typeof(T));
}
