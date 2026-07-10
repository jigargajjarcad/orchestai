using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Application.Configuration;

namespace OrchestAI.Application.Commands.RequestPostHocScoring;

// Resolves trace selection to a concrete AgentExecutionId list at request time (not lazily by
// the worker) — see ADR-013 Decision 3. This makes the MaxTraces cap enforcement a single choke
// point and makes a given EvalRunId's scope reproducible regardless of what production traffic
// arrives after the request is enqueued.
public sealed class RequestPostHocScoringHandler
    : IRequestHandler<RequestPostHocScoringCommand, RequestPostHocScoringResponse>
{
    private readonly IAgentExecutionRepository _executionRepository;
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalRunQueue _queue;
    private readonly IOptions<EvalOptions> _options;
    private readonly ILogger<RequestPostHocScoringHandler> _logger;

    public RequestPostHocScoringHandler(
        IAgentExecutionRepository executionRepository,
        IEvalRunRepository runRepository,
        IEvalRunQueue queue,
        IOptions<EvalOptions> options,
        ILogger<RequestPostHocScoringHandler> logger)
    {
        _executionRepository = executionRepository;
        _runRepository = runRepository;
        _queue = queue;
        _options = options;
        _logger = logger;
    }

    public async Task<RequestPostHocScoringResponse> Handle(
        RequestPostHocScoringCommand request, CancellationToken cancellationToken)
    {
        if (request.ScorerType != EvalScorerType.LlmJudge)
            throw new ValidationException(
                nameof(request.ScorerType),
                "Post-hoc scoring is judge-only — RuleBasedScorer requires a predefined EvalCase's " +
                "ExpectedCriteria, which doesn't exist for arbitrary historical traces. See ADR-013.");

        if (string.IsNullOrWhiteSpace(request.Rubric))
            throw new ValidationException(nameof(request.Rubric), "Rubric is required for post-hoc judge scoring.");

        var hasDateRange = request.DateFrom.HasValue && request.DateTo.HasValue;
        var hasExplicitIds = request.TraceIds is { Count: > 0 };
        if (!hasDateRange && !hasExplicitIds)
            throw new ValidationException(
                nameof(request.DateFrom),
                "A post-hoc scoring request must specify either a date range (DateFrom and DateTo) " +
                "or an explicit TraceIds list — AgentType alone does not bound the selection.");

        if (request.MaxTraces <= 0 || request.MaxTraces > _options.Value.MaxPostHocTracesPerRequestCeiling)
            throw new ValidationException(
                nameof(request.MaxTraces),
                $"MaxTraces must be between 1 and {_options.Value.MaxPostHocTracesPerRequestCeiling}.");

        var selection = await _executionRepository.SelectForPostHocScoringAsync(
            request.DateFrom, request.DateTo, request.AgentType, request.TraceIds, request.MaxTraces, cancellationToken)
            .ConfigureAwait(false);

        if (selection.TotalMatched > request.MaxTraces)
            throw new ValidationException(
                nameof(request.MaxTraces),
                $"Selection matched {selection.TotalMatched} traces, exceeding the requested cap of " +
                $"{request.MaxTraces}. Narrow the date range or trace ID list.");

        if (selection.AgentExecutionIds.Count == 0)
            throw new ValidationException(nameof(request.TraceIds), "No completed traces matched the selection criteria.");

        var selectionCriteriaJson = JsonSerializer.Serialize(new
        {
            dateFrom = request.DateFrom,
            dateTo = request.DateTo,
            agentType = request.AgentType?.ToString(),
            explicitTraceIds = request.TraceIds,
            resolvedTraceIds = selection.AgentExecutionIds,
            passThreshold = request.PassThreshold
        });

        var subjectVersion = $"posthoc-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var run = EvalRun.CreatePostHoc(subjectVersion, request.Rubric, selectionCriteriaJson, request.ForceRescore);
        await _runRepository.AddAsync(run, cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(run.Id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Enqueued post-hoc scoring run {RunId} for {TraceCount} traces (agentType={AgentType})",
            run.Id, selection.AgentExecutionIds.Count, request.AgentType);

        return new RequestPostHocScoringResponse(
            run.Id, run.Status.ToString(), selection.AgentExecutionIds.Count, run.TriggeredAt);
    }
}
