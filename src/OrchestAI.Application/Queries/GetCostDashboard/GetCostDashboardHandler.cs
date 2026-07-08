using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetCostDashboard;

// Hybrid read per ADR-011: CostRollup (pre-aggregated, fast) for any day strictly before
// today, raw CostLedger aggregated on the fly for today (rollups lag by up to the background
// job's interval, but a just-completed run must be immediately reflected).
public sealed class GetCostDashboardHandler : IRequestHandler<GetCostDashboardQuery, GetCostDashboardResponse>
{
    private readonly ICostRollupRepository _costRollupRepository;
    private readonly ICostLedgerRepository _costLedgerRepository;

    public GetCostDashboardHandler(
        ICostRollupRepository costRollupRepository, ICostLedgerRepository costLedgerRepository)
    {
        _costRollupRepository = costRollupRepository;
        _costLedgerRepository = costLedgerRepository;
    }

    public async Task<GetCostDashboardResponse> Handle(
        GetCostDashboardQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var breakdown = new List<CostBreakdownEntryDto>();

        var rollupTo = request.To < today ? request.To : today.AddDays(-1);
        if (rollupTo >= request.From)
        {
            var rollups = await _costRollupRepository
                .GetByDateRangeAsync(request.From, rollupTo, request.UserId, cancellationToken)
                .ConfigureAwait(false);

            breakdown.AddRange(rollups.Select(r => new CostBreakdownEntryDto(
                r.Date, r.AgentType.ToString(), r.Model, r.InputTokens, r.OutputTokens,
                r.CostUsd, r.ExecutionCount, IsLive: false)));
        }

        if (request.To >= today && request.From <= today)
        {
            var todayAggregates = await _costLedgerRepository
                .GetDailyAggregatesAsync(today, today, cancellationToken)
                .ConfigureAwait(false);

            breakdown.AddRange(todayAggregates
                .Where(a => a.UserId == request.UserId)
                .Select(a => new CostBreakdownEntryDto(
                    a.Date, a.AgentType.ToString(), a.Model, a.InputTokens, a.OutputTokens,
                    a.CostUsd, a.ExecutionCount, IsLive: true)));
        }

        breakdown = breakdown.OrderBy(b => b.Date).ThenBy(b => b.AgentType).ThenBy(b => b.Model).ToList();

        return new GetCostDashboardResponse(
            request.From,
            request.To,
            breakdown.Sum(b => b.CostUsd),
            breakdown.Sum(b => b.ExecutionCount),
            breakdown.AsReadOnly());
    }
}
