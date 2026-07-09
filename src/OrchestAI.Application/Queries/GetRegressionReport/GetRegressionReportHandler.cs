using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetRegressionReport;

// Baseline promotion is manual per ADR-012 — EvalRun.BaselineRunId is set explicitly at
// trigger time, never auto-selected. A run with no baseline has nothing to diff against, so
// this fails loudly (ValidationException) instead of returning a response shaped like a
// report with every field zeroed out, which would be silently misleading.
public sealed class GetRegressionReportHandler : IRequestHandler<GetRegressionReportQuery, GetRegressionReportResponse>
{
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalSuiteRepository _suiteRepository;
    private readonly IEvalResultRepository _resultRepository;
    private readonly ILogger<GetRegressionReportHandler> _logger;

    public GetRegressionReportHandler(
        IEvalRunRepository runRepository,
        IEvalSuiteRepository suiteRepository,
        IEvalResultRepository resultRepository,
        ILogger<GetRegressionReportHandler> logger)
    {
        _runRepository = runRepository;
        _suiteRepository = suiteRepository;
        _resultRepository = resultRepository;
        _logger = logger;
    }

    public async Task<GetRegressionReportResponse> Handle(
        GetRegressionReportQuery request, CancellationToken cancellationToken)
    {
        var currentRun = await _runRepository.GetByIdAsync(request.EvalRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), request.EvalRunId);

        if (currentRun.BaselineRunId is not { } baselineRunId)
            throw new ValidationException(
                nameof(currentRun.BaselineRunId),
                $"Eval run {currentRun.Id} has no baseline set — a regression report needs an explicit baseline_run_id.");

        var baselineRun = await _runRepository.GetByIdAsync(baselineRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), baselineRunId);

        var suite = await _suiteRepository.GetByIdWithCasesAsync(currentRun.SuiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalSuite), currentRun.SuiteId);
        var thresholdByCaseId = suite.Cases.ToDictionary(c => c.Id, c => c.RegressionThreshold);

        var currentResults = await _resultRepository.GetByRunIdAsync(currentRun.Id, cancellationToken).ConfigureAwait(false);
        var baselineResults = await _resultRepository.GetByRunIdAsync(baselineRun.Id, cancellationToken).ConfigureAwait(false);
        var baselineByCaseId = baselineResults.ToDictionary(r => r.EvalCaseId);

        var caseDiffs = currentResults.Select(current =>
        {
            var hasBaseline = baselineByCaseId.TryGetValue(current.EvalCaseId, out var baseline);
            var scoreDelta = hasBaseline ? baseline!.Score - current.Score : (decimal?)null;
            var threshold = thresholdByCaseId.GetValueOrDefault(current.EvalCaseId, 0m);
            var regressed = hasBaseline && scoreDelta > threshold;

            return new CaseRegressionDto(
                current.EvalCaseId, current.Score, hasBaseline ? baseline!.Score : null,
                scoreDelta, regressed, IsNewCase: !hasBaseline);
        }).ToList();

        var currentPassRate = PassRate(currentResults);
        var baselinePassRate = PassRate(baselineResults);

        _logger.LogInformation(
            "Regression report for run {RunId} vs baseline {BaselineRunId}: {RegressedCount} of {CaseCount} cases regressed",
            currentRun.Id, baselineRun.Id, caseDiffs.Count(d => d.Regressed), caseDiffs.Count);

        return new GetRegressionReportResponse(
            currentRun.Id, baselineRun.Id, currentPassRate, baselinePassRate,
            currentPassRate - baselinePassRate, caseDiffs);
    }

    private static decimal PassRate(IReadOnlyList<EvalResult> results) =>
        results.Count == 0 ? 0m : (decimal)results.Count(r => r.Passed) / results.Count;
}
