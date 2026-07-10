using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetPostHocScoringSummary;

public sealed class GetPostHocScoringSummaryHandler
    : IRequestHandler<GetPostHocScoringSummaryQuery, GetPostHocScoringSummaryResponse>
{
    private static readonly (decimal Lower, decimal Upper)[] Buckets =
    [
        (0.0m, 0.2m), (0.2m, 0.4m), (0.4m, 0.6m), (0.6m, 0.8m), (0.8m, 1.0m)
    ];

    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalResultRepository _resultRepository;

    public GetPostHocScoringSummaryHandler(IEvalRunRepository runRepository, IEvalResultRepository resultRepository)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
    }

    public async Task<GetPostHocScoringSummaryResponse> Handle(
        GetPostHocScoringSummaryQuery request, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetByIdAsync(request.EvalRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), request.EvalRunId);

        if (run.Source != EvalRunSource.PostHoc)
            throw new ValidationException(nameof(run.Source), $"Eval run {run.Id} is not a post-hoc run.");

        var results = await _resultRepository.GetByRunIdAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var passedCount = results.Count(r => r.Passed);

        var distribution = Buckets.Select(b => new ScoreDistributionBucketDto(
            $"{b.Lower:0.0}-{b.Upper:0.0}",
            results.Count(r => r.Score >= b.Lower && (b.Upper == 1.0m ? r.Score <= b.Upper : r.Score < b.Upper))))
            .ToList();

        var passRate = results.Count == 0 ? 0m : (decimal)passedCount / results.Count;

        return new GetPostHocScoringSummaryResponse(
            run.Id, run.Status.ToString(), results.Count, run.SkippedAlreadyScoredCount,
            passedCount, passRate, distribution, run.TriggeredAt, run.CompletedAt);
    }
}
