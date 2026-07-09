using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetEvalSuites;

public sealed class GetEvalSuitesHandler : IRequestHandler<GetEvalSuitesQuery, GetEvalSuitesResponse>
{
    private readonly IEvalSuiteRepository _repository;

    public GetEvalSuitesHandler(IEvalSuiteRepository repository) => _repository = repository;

    public async Task<GetEvalSuitesResponse> Handle(GetEvalSuitesQuery request, CancellationToken cancellationToken)
    {
        var suites = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return new GetEvalSuitesResponse(suites
            .Select(s => new EvalSuiteSummaryDto(s.Id, s.Name, s.Description, s.TargetAgentType.ToString(), s.CreatedAt))
            .ToList());
    }
}
