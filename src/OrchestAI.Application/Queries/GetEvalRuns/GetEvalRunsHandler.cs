using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetEvalRuns;

public sealed class GetEvalRunsHandler : IRequestHandler<GetEvalRunsQuery, GetEvalRunsResponse>
{
    private readonly IEvalRunRepository _repository;

    public GetEvalRunsHandler(IEvalRunRepository repository) => _repository = repository;

    public async Task<GetEvalRunsResponse> Handle(GetEvalRunsQuery request, CancellationToken cancellationToken)
    {
        var runs = await _repository.GetBySuiteIdAsync(request.SuiteId, cancellationToken).ConfigureAwait(false);

        return new GetEvalRunsResponse(runs
            .Select(r => new EvalRunSummaryDto(
                r.Id, r.Status.ToString(), r.SubjectVersion, r.BaselineRunId, r.TriggeredAt, r.CompletedAt))
            .ToList());
    }
}
