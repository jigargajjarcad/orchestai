using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Interfaces;

public interface IAgentFactory
{
    IAgent Create(AgentType agentType);
}
