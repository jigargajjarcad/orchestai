using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.RunEvalSuite;

public sealed class RunEvalSuiteHandler : IRequestHandler<RunEvalSuiteCommand, RunEvalSuiteResponse>
{
    private readonly IEvalSuiteRepository _suiteRepository;
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalRunQueue _queue;
    private readonly ILogger<RunEvalSuiteHandler> _logger;

    public RunEvalSuiteHandler(
        IEvalSuiteRepository suiteRepository,
        IEvalRunRepository runRepository,
        IEvalRunQueue queue,
        ILogger<RunEvalSuiteHandler> logger)
    {
        _suiteRepository = suiteRepository;
        _runRepository = runRepository;
        _queue = queue;
        _logger = logger;
    }

    public async Task<RunEvalSuiteResponse> Handle(RunEvalSuiteCommand request, CancellationToken cancellationToken)
    {
        var suite = await _suiteRepository.GetByIdAsync(request.SuiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalSuite), request.SuiteId);

        if (string.IsNullOrWhiteSpace(request.SubjectVersion))
            throw new ValidationException(nameof(request.SubjectVersion), "SubjectVersion is required.");

        if (request.BaselineRunId is { } baselineRunId)
        {
            _ = await _runRepository.GetByIdAsync(baselineRunId, cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException(nameof(EvalRun), baselineRunId);
        }

        var run = EvalRun.Create(suite.Id, request.SubjectVersion, request.BaselineRunId);
        await _runRepository.AddAsync(run, cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(run.Id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Enqueued eval run {RunId} for suite {SuiteId} (subject={SubjectVersion}, baseline={BaselineRunId})",
            run.Id, suite.Id, request.SubjectVersion, request.BaselineRunId);

        return new RunEvalSuiteResponse(run.Id, suite.Id, run.Status.ToString(), run.BaselineRunId, run.TriggeredAt);
    }
}
