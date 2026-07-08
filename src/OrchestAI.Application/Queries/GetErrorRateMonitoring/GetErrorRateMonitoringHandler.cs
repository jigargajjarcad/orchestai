using MediatR;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetErrorRateMonitoring;

public sealed class GetErrorRateMonitoringHandler
    : IRequestHandler<GetErrorRateMonitoringQuery, GetErrorRateMonitoringResponse>
{
    private readonly IAgentExecutionRepository _agentExecutionRepository;
    private readonly IMcpToolCallRepository _mcpToolCallRepository;
    private readonly IAgentRetryAttemptRepository _retryAttemptRepository;

    public GetErrorRateMonitoringHandler(
        IAgentExecutionRepository agentExecutionRepository,
        IMcpToolCallRepository mcpToolCallRepository,
        IAgentRetryAttemptRepository retryAttemptRepository)
    {
        _agentExecutionRepository = agentExecutionRepository;
        _mcpToolCallRepository = mcpToolCallRepository;
        _retryAttemptRepository = retryAttemptRepository;
    }

    public async Task<GetErrorRateMonitoringResponse> Handle(
        GetErrorRateMonitoringQuery request, CancellationToken cancellationToken)
    {
        var executionStats = await _agentExecutionRepository
            .GetErrorStatsAsync(request.UserId, request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        var executionIds = executionStats.Select(s => s.AgentExecutionId).ToList();
        var retryAttempts = await _retryAttemptRepository
            .GetByAgentExecutionIdsAsync(executionIds, cancellationToken)
            .ConfigureAwait(false);

        var executionIdToAgentType = executionStats.ToDictionary(s => s.AgentExecutionId, s => s.AgentType);
        var retryCountsByAgentType = retryAttempts
            .GroupBy(r => executionIdToAgentType.GetValueOrDefault(r.AgentExecutionId))
            .ToDictionary(g => g.Key, g => g.Count());

        var agentRates = executionStats
            .GroupBy(s => s.AgentType)
            .Select(g =>
            {
                var failed = g.Where(s => s.Status == ExecutionStatus.Failed).ToList();
                return new AgentErrorRateDto(
                    g.Key.ToString(),
                    g.Count(),
                    failed.Count,
                    g.Any() ? (double)failed.Count / g.Count() : 0,
                    failed
                        .Where(s => s.ErrorCategory.HasValue)
                        .GroupBy(s => s.ErrorCategory!.Value.ToString())
                        .ToDictionary(cg => cg.Key, cg => cg.Count()),
                    retryCountsByAgentType.GetValueOrDefault(g.Key));
            })
            .OrderByDescending(a => a.FailureRate)
            .ToList()
            .AsReadOnly();

        var toolStats = await _mcpToolCallRepository
            .GetErrorStatsAsync(request.UserId, request.From, request.To, cancellationToken)
            .ConfigureAwait(false);

        var toolRates = toolStats
            .GroupBy(s => s.ToolName)
            .Select(g =>
            {
                var failed = g.Where(s => !s.Success).ToList();
                return new ToolErrorRateDto(
                    g.Key,
                    g.Count(),
                    failed.Count,
                    g.Any() ? (double)failed.Count / g.Count() : 0,
                    failed
                        .Where(s => s.ErrorCategory.HasValue)
                        .GroupBy(s => s.ErrorCategory!.Value.ToString())
                        .ToDictionary(cg => cg.Key, cg => cg.Count()));
            })
            .OrderByDescending(t => t.FailureRate)
            .ToList()
            .AsReadOnly();

        return new GetErrorRateMonitoringResponse(request.From, request.To, agentRates, toolRates);
    }
}
