using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Commands.DeleteAgentMemory;
using OrchestAI.Application.DTOs;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetUserMemories;
using OrchestAI.Domain.Enums;

namespace OrchestAI.API.Controllers;

[ApiController]
[Route("api/v1/users/{userId:guid}/memories")]
[Produces("application/json")]
public sealed class MemoriesController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<MemoriesController> _logger;

    public MemoriesController(IMediator mediator, ILogger<MemoriesController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    /// <summary>Lists all memories for a user.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AgentMemoryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var memories = await _mediator.Send(new GetUserMemoriesQuery(userId), cancellationToken);
        return Ok(memories);
    }

    /// <summary>Lists a user's memories scoped to a specific agent type.</summary>
    [HttpGet("{agentType}")]
    [ProducesResponseType(typeof(IReadOnlyList<AgentMemoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByAgentTypeAsync(
        Guid userId, string agentType, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<AgentType>(agentType, ignoreCase: true, out var parsedAgentType))
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Agent Type",
                Detail = $"'{agentType}' is not a recognized agent type.",
                Status = StatusCodes.Status400BadRequest
            });

        var memories = await _mediator.Send(new GetUserMemoriesQuery(userId, parsedAgentType), cancellationToken);
        return Ok(memories);
    }

    /// <summary>Deletes a single memory entry.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Send(new DeleteAgentMemoryCommand(id), cancellationToken);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
    }
}
