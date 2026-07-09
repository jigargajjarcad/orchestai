using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateEvalSuite;

public sealed class CreateEvalSuiteHandler : IRequestHandler<CreateEvalSuiteCommand, CreateEvalSuiteResponse>
{
    private readonly IEvalSuiteRepository _repository;
    private readonly ILogger<CreateEvalSuiteHandler> _logger;

    public CreateEvalSuiteHandler(IEvalSuiteRepository repository, ILogger<CreateEvalSuiteHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<CreateEvalSuiteResponse> Handle(
        CreateEvalSuiteCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException(nameof(request.Name), "Name is required.");

        var suite = EvalSuite.Create(request.Name, request.Description, request.TargetAgentType);
        await _repository.AddAsync(suite, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Created eval suite {SuiteId} '{Name}' targeting {AgentType}",
            suite.Id, suite.Name, suite.TargetAgentType);

        return new CreateEvalSuiteResponse(
            suite.Id, suite.Name, suite.Description, suite.TargetAgentType.ToString(), suite.CreatedAt);
    }
}
