using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.AddEvalCase;

public sealed class AddEvalCaseHandler : IRequestHandler<AddEvalCaseCommand, AddEvalCaseResponse>
{
    private readonly IEvalSuiteRepository _suiteRepository;
    private readonly ILogger<AddEvalCaseHandler> _logger;

    public AddEvalCaseHandler(IEvalSuiteRepository suiteRepository, ILogger<AddEvalCaseHandler> logger)
    {
        _suiteRepository = suiteRepository;
        _logger = logger;
    }

    public async Task<AddEvalCaseResponse> Handle(AddEvalCaseCommand request, CancellationToken cancellationToken)
    {
        var suite = await _suiteRepository.GetByIdAsync(request.SuiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalSuite), request.SuiteId);

        if (request.RegressionThreshold < 0)
            throw new ValidationException(
                nameof(request.RegressionThreshold), "RegressionThreshold must not be negative.");

        var evalCase = EvalCase.Create(
            suite.Id, request.InputPayloadJson, request.ExpectedCriteriaJson,
            request.ScorerType, request.RegressionThreshold, request.Tags);

        await _suiteRepository.AddCaseAsync(evalCase, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Added eval case {CaseId} to suite {SuiteId} (scorer={ScorerType})",
            evalCase.Id, suite.Id, evalCase.ScorerType);

        return new AddEvalCaseResponse(
            evalCase.Id, evalCase.SuiteId, evalCase.ScorerType.ToString(),
            evalCase.RegressionThreshold, evalCase.CreatedAt);
    }
}
