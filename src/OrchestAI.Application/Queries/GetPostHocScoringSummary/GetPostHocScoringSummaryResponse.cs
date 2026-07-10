namespace OrchestAI.Application.Queries.GetPostHocScoringSummary;

public sealed record GetPostHocScoringSummaryResponse(
    Guid EvalRunId,
    string Status,
    int ScoredCount,
    int SkippedAlreadyScoredCount,
    int PassedCount,
    decimal PassRate,
    IReadOnlyList<ScoreDistributionBucketDto> ScoreDistribution,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? CompletedAt);

public sealed record ScoreDistributionBucketDto(string Range, int Count);
