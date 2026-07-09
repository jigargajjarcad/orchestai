using MediatR;

namespace OrchestAI.Application.Queries.GetRegressionReport;

public sealed record GetRegressionReportQuery(Guid EvalRunId) : IRequest<GetRegressionReportResponse>;
