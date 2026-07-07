using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Commands.ResumeOrchestrationTask;

public sealed record ResumeOrchestrationTaskResponse(
    Guid TaskId,
    IReadOnlyList<AgentType> ResumedFrom,
    IReadOnlyList<AgentType> SkippedAgents
);
