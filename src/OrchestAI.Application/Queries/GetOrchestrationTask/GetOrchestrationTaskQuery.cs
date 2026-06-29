using MediatR;

namespace OrchestAI.Application.Queries.GetOrchestrationTask;

public sealed record GetOrchestrationTaskQuery(
    Guid TaskId,
    bool IncludeMessages = false,
    bool IncludeToolCalls = false
) : IRequest<GetOrchestrationTaskResponse?>;
