using MediatR;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Commands.CreateEvalSuite;

public sealed record CreateEvalSuiteCommand(
    string Name,
    string Description,
    AgentType TargetAgentType
) : IRequest<CreateEvalSuiteResponse>;
