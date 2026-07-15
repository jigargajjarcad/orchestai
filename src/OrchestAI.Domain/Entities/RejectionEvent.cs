using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

// A lightweight log of denials — deliberately separate from the full trace/cost event
// pipeline (AgentExecution/CostLedger), not an execution record. ITenantScoped: always created
// inside a live ambient-tenant scope (see RejectionResponder), never given TenantId directly.
public sealed class RejectionEvent : ITenantScoped
{
    private RejectionEvent() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public RejectionReason Reason { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string? RequestId { get; private set; }
    public string? TraceId { get; private set; }
    public Guid? ApiKeyId { get; private set; }
    public string DetailsJson { get; private set; } = "{}";

    public static RejectionEvent Create(
        RejectionReason reason, string? requestId, string? traceId, Guid? apiKeyId, string detailsJson)
    {
        return new RejectionEvent
        {
            Id = Guid.NewGuid(),
            Reason = reason,
            OccurredAt = DateTimeOffset.UtcNow,
            RequestId = requestId,
            TraceId = traceId,
            ApiKeyId = apiKeyId,
            DetailsJson = detailsJson
        };
    }
}
