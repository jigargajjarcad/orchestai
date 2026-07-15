namespace OrchestAI.Application.Queries.GetRejections;

public sealed record RejectionEntryDto(
    Guid Id,
    string Reason,
    DateTimeOffset OccurredAt,
    string? RequestId,
    string? TraceId,
    Guid? ApiKeyId,
    string DetailsJson);

public sealed record GetRejectionsResponse(IReadOnlyList<RejectionEntryDto> Rejections);
