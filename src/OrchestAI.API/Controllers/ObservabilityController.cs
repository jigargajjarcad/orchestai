using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Queries.GetCostDashboard;
using OrchestAI.Application.Queries.GetErrorRateMonitoring;
using OrchestAI.Application.Queries.GetRecentTasks;

namespace OrchestAI.API.Controllers;

[ApiController]
[Route("api/v1/users/{userId:guid}/observability")]
[Produces("application/json")]
public sealed class ObservabilityController : ControllerBase
{
    private readonly IMediator _mediator;

    public ObservabilityController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Cost breakdown by day/agent/model — rollups for past days, live data for today.</summary>
    [HttpGet("cost-dashboard")]
    [ProducesResponseType(typeof(GetCostDashboardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCostDashboardAsync(
        Guid userId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Date Range",
                Detail = "'to' must not be earlier than 'from'.",
                Status = StatusCodes.Status400BadRequest
            });

        var response = await _mediator.Send(new GetCostDashboardQuery(userId, from, to), cancellationToken);
        return Ok(response);
    }

    /// <summary>Agent/tool failure rates over time, categorized by failure reason, with retry counts.</summary>
    [HttpGet("error-rates")]
    [ProducesResponseType(typeof(GetErrorRateMonitoringResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetErrorRatesAsync(
        Guid userId,
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        if (to < from)
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Date Range",
                Detail = "'to' must not be earlier than 'from'.",
                Status = StatusCodes.Status400BadRequest
            });

        var response = await _mediator.Send(new GetErrorRateMonitoringQuery(userId, from, to), cancellationToken);
        return Ok(response);
    }

    /// <summary>Most recent tasks for a user — powers the observability views' task picker.</summary>
    [HttpGet("/api/v1/users/{userId:guid}/tasks")]
    [ProducesResponseType(typeof(IReadOnlyList<RecentTaskDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentTasksAsync(
        Guid userId, [FromQuery] int limit, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new GetRecentTasksQuery(userId, limit <= 0 ? 20 : limit), cancellationToken);
        return Ok(response);
    }
}
