namespace OrchestAI.Domain.Models;

// TenantId travels with the queued item because EvalRunBackgroundWorker processes this entirely
// outside any HTTP request — there is no ambient tenant context to infer once dequeued; it must
// be captured explicitly at enqueue time. See ADR-014 confirmation #5.
public sealed record EvalRunQueueItem(Guid EvalRunId, Guid TenantId);
