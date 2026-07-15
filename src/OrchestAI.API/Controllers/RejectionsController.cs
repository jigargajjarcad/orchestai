using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Queries.GetRejections;

namespace OrchestAI.API.Controllers;

[ApiController]
[Route("api/v1/rejections")]
[Produces("application/json")]
public sealed class RejectionsController : ControllerBase
{
    private readonly IMediator _mediator;

    public RejectionsController(IMediator mediator) => _mediator = mediator;

    /// <summary>Recent rate-limit/concurrency/budget/agent-cap/queue rejections for the caller's tenant.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetRejectionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAsync([FromQuery] int limit, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetRejectionsQuery(limit <= 0 ? 50 : limit), cancellationToken);
        return Ok(response);
    }
}
