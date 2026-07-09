using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetEvalRunResults;

public sealed class GetEvalRunResultsHandler : IRequestHandler<GetEvalRunResultsQuery, GetEvalRunResultsResponse>
{
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalResultRepository _resultRepository;
    private readonly ILogger<GetEvalRunResultsHandler> _logger;

    public GetEvalRunResultsHandler(
        IEvalRunRepository runRepository, IEvalResultRepository resultRepository,
        ILogger<GetEvalRunResultsHandler> logger)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
        _logger = logger;
    }

    public async Task<GetEvalRunResultsResponse> Handle(
        GetEvalRunResultsQuery request, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetByIdAsync(request.EvalRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), request.EvalRunId);

        var results = await _resultRepository.GetByRunIdAsync(run.Id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Read {Count} eval results for run {RunId}", results.Count, run.Id);

        return new GetEvalRunResultsResponse(
            run.Id, run.Status.ToString(),
            results.Select(r => new EvalResultDto(
                r.EvalCaseId, r.AgentExecutionId, r.ScorerType.ToString(), r.ScorerVersion,
                r.Score, r.Passed, r.ScorerOutput, r.ScoredAt))
                .ToList());
    }
}
