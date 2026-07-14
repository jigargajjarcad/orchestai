using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class EvalSuite : ITenantScoped
{
    private EvalSuite() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public AgentType TargetAgentType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<EvalCase> _cases = [];
    public IReadOnlyCollection<EvalCase> Cases => _cases.AsReadOnly();

    public static EvalSuite Create(string name, string description, AgentType targetAgentType)
    {
        return new EvalSuite
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            TargetAgentType = targetAgentType,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
