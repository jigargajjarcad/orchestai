using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetRejections;

public sealed class GetRejectionsHandler : IRequestHandler<GetRejectionsQuery, GetRejectionsResponse>
{
    private readonly IRejectionEventRepository _repository;

    public GetRejectionsHandler(IRejectionEventRepository repository) => _repository = repository;

    public async Task<GetRejectionsResponse> Handle(GetRejectionsQuery request, CancellationToken cancellationToken)
    {
        var rejections = await _repository.GetRecentAsync(request.Limit, cancellationToken).ConfigureAwait(false);

        return new GetRejectionsResponse(rejections
            .Select(r => new RejectionEntryDto(
                r.Id, r.Reason.ToString(), r.OccurredAt, r.RequestId, r.TraceId, r.ApiKeyId, r.DetailsJson))
            .ToList());
    }
}
