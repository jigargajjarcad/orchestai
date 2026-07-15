namespace OrchestAI.Application.Exceptions;

// Thrown by IIdempotencyRecordRepository.AddAsync (Task 4 review fix) for the ONE race the
// brief's design note explicitly accepts: two genuinely concurrent first-uses of the same
// brand-new Idempotency-Key both pass CreateOrchestrationTaskHandler's "not found" check before
// either commits its IdempotencyRecord row. The unique (TenantId, IdempotencyKey) index accepts
// exactly one insert; the loser's freshly-created OrchestrationTask becomes an orphaned
// (harmless) Pending row, and CreateOrchestrationTaskHandler catches this exception and falls
// back to returning the task the WINNING record already points to — exactly like a normal
// replay, per ADR-015, instead of surfacing an unhandled 500.
public sealed class IdempotencyKeyConflictException : Exception
{
    public Guid ExistingTaskId { get; }

    public IdempotencyKeyConflictException(Guid existingTaskId)
        : base($"Idempotency-Key already claimed by task '{existingTaskId}'.")
    {
        ExistingTaskId = existingTaskId;
    }
}
