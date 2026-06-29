namespace OrchestAI.Domain.Events;

public sealed record SseEvent(
    string Event,
    Guid TaskId,
    object Payload,
    DateTimeOffset Timestamp
);
