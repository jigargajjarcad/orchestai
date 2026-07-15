using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// ITenantScoped — always created inside a live ambient-tenant scope (the CreateOrchestrationTask
// request), never given TenantId directly. A key is only unique within one tenant's own scope.
public sealed class IdempotencyRecord : ITenantScoped
{
    private IdempotencyRecord() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public Guid TaskId { get; private set; }
    public string RequestPayloadHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    public static IdempotencyRecord Create(string idempotencyKey, Guid taskId, string requestPayloadHash, TimeSpan ttl)
    {
        var now = DateTimeOffset.UtcNow;
        return new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            TaskId = taskId,
            RequestPayloadHash = requestPayloadHash,
            CreatedAt = now,
            ExpiresAt = now.Add(ttl)
        };
    }
}
