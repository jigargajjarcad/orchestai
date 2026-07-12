using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class AgentMessage : ITenantScoped
{
    private AgentMessage() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AgentExecutionId { get; private set; }
    public MessageRole Role { get; private set; }
    public string Content { get; private set; } = string.Empty;
    public int SequenceOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public AgentExecution AgentExecution { get; private set; } = null!;

    public static AgentMessage Create(
        Guid agentExecutionId,
        MessageRole role,
        string content,
        int sequenceOrder)
    {
        return new AgentMessage
        {
            Id = Guid.NewGuid(),
            AgentExecutionId = agentExecutionId,
            Role = role,
            Content = content,
            SequenceOrder = sequenceOrder,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
