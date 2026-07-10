namespace OrchestAI.Application.Commands.RequestPostHocScoring;

public sealed record RequestPostHocScoringResponse(
    Guid EvalRunId, string Status, int ResolvedTraceCount, DateTimeOffset TriggeredAt);
