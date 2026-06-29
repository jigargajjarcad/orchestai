using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetOrchestrationTask;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
public sealed class TasksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IOrchestrationEventBus _eventBus;
    private readonly ILogger<TasksController> _logger;

    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public TasksController(
        IMediator mediator,
        IOrchestrationEventBus eventBus,
        ILogger<TasksController> logger)
    {
        _mediator = mediator;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>Creates a new orchestration task.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrchestrationTaskResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrchestrationTaskCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction("GetById", new { id = response.Id }, response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for CreateOrchestrationTask: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Gets an orchestration task by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetOrchestrationTaskResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(
        Guid id,
        [FromQuery] bool includeMessages = false,
        [FromQuery] bool includeToolCalls = false,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetOrchestrationTaskQuery(id, includeMessages, includeToolCalls), cancellationToken);

        if (response is null)
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Orchestration task {id} does not exist.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(response);
    }

    /// <summary>Starts agent execution for a pending task.</summary>
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(typeof(StartOrchestrationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult StartAsync(
        Guid id,
        [FromServices] IServiceScopeFactory scopeFactory)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            try
            {
                await mediator.Send(new StartOrchestrationCommand(id), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning("Start failed — task not found: {TaskId}", ex.EntityId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Start failed — invalid state: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error starting task {TaskId}", id);
            }
        });

        return Accepted(new StartOrchestrationResponse(id, []));
    }

    /// <summary>SSE stream for real-time task execution events.</summary>
    [HttpGet("{id:guid}/stream")]
    public async Task StreamAsync(Guid id, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");
        Response.Headers.Append("Connection", "keep-alive");

        _logger.LogInformation("SSE client connected for task {TaskId}", id);

        await foreach (var evt in _eventBus.SubscribeAsync(id, cancellationToken))
        {
            var json = JsonSerializer.Serialize(evt, SseJsonOptions);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken).ConfigureAwait(false);
            await Response.Body.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("SSE stream ended for task {TaskId}", id);
    }

    /// <summary>Health check endpoint.</summary>
    [HttpGet("/api/v1/health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow });
    }
}
