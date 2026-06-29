namespace OrchestAI.Application.Commands.StartOrchestration;

public sealed record StartOrchestrationResponse(
    Guid TaskId,
    IReadOnlyList<Guid> AgentExecutionIds
);
