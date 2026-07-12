using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class AgentMemory : IHasUpdatedAt, ITenantScoped
{
    private AgentMemory() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public AgentType AgentType { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public int Importance { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public User User { get; private set; } = null!;

    public static AgentMemory Create(
        Guid userId,
        AgentType agentType,
        string key,
        string value,
        int importance = 5,
        DateTimeOffset? expiresAt = null)
    {
        return new AgentMemory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AgentType = agentType,
            Key = key,
            Value = value,
            Importance = Math.Clamp(importance, 1, 10),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = expiresAt
        };
    }

    public void UpdateValue(string newValue, int? importance = null)
    {
        Value = newValue;
        if (importance.HasValue)
            Importance = Math.Clamp(importance.Value, 1, 10);
    }

    public bool IsExpired() => ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;
}
