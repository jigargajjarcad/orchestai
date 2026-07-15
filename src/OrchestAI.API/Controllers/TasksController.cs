using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Commands.AdmitOrchestrationTask;
using OrchestAI.Application.Commands.ApproveOrchestrationTask;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Commands.RejectOrchestrationTask;
using OrchestAI.Application.Commands.ResumeOrchestrationTask;
using OrchestAI.Application.Commands.StartOrchestration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetExecutionSummary;
using OrchestAI.Application.Queries.GetExecutionTimeline;
using OrchestAI.Application.Queries.GetOrchestrationTask;
using OrchestAI.Application.Queries.GetTaskComparison;
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

    /// <summary>Creates a new orchestration task. Supports an optional Idempotency-Key header.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrchestrationTaskResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateOrchestrationTaskCommand bodyCommand,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var command = bodyCommand with { IdempotencyKey = idempotencyKey };

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
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails { Title = "Conflict", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
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

    /// <summary>Admits (concurrency/budget checks) and starts agent execution for a pending
    /// task. Admission is synchronous — a 429/404/409 here means no dispatch was ever queued;
    /// agent dispatch itself continues in the background after a 202.</summary>
    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(typeof(StartOrchestrationResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> StartAsync(
        Guid id,
        [FromServices] IServiceScopeFactory scopeFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Send(new AdmitOrchestrationTaskCommand(id), cancellationToken);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails { Title = "Conflict", Detail = ex.Message, Status = StatusCodes.Status409Conflict });
        }
        // TenantLimitExceededException is deliberately not caught here — it propagates to the
        // global TenantLimitExceededExceptionHandler (Task 2), the one place that builds the
        // unified 429 response and writes the RejectionEvent. Catching it locally would
        // duplicate that logic in a second place.

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
                _logger.LogWarning("Dispatch failed — task not found: {TaskId}", ex.EntityId);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning("Dispatch failed — invalid state: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error dispatching task {TaskId}", id);
            }
        });

        return Accepted(new StartOrchestrationResponse(id, []));
    }

    /// <summary>Resumes a Failed task from its first agent without a saved checkpoint.</summary>
    [HttpPost("{id:guid}/resume")]
    [ProducesResponseType(typeof(ResumeOrchestrationTaskResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult ResumeAsync(
        Guid id,
        [FromServices] IServiceScopeFactory scopeFactory)
    {
        _ = Task.Run(async () =>
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            try
            {
                await mediator.Send(new ResumeOrchestrationTaskCommand(id), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (NotFoundException ex)
            {
                _logger.LogWarning("Resume failed — task not found: {TaskId}", ex.EntityId);
            }
            catch (ConflictException ex)
            {
                _logger.LogWarning("Resume failed — invalid state: {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error resuming task {TaskId}", id);
            }
        });

        // Actual skipped/resuming agent lists are only known once the handler loads
        // checkpoints — the client learns them from the task_resumed SSE event.
        return Accepted(new ResumeOrchestrationTaskResponse(id, [], []));
    }

    /// <summary>Approves a task that is waiting for human review, allowing agent dispatch to resume.</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveAsync(
        Guid id,
        [FromBody] ApprovalRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Send(new ApproveOrchestrationTaskCommand(id, request?.Note), cancellationToken);
            return Ok();
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
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>Rejects a task that is waiting for human review, marking it failed without dispatching agents.</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectAsync(
        Guid id,
        [FromBody] ApprovalRequest? request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _mediator.Send(new RejectOrchestrationTaskCommand(id, request?.Note), cancellationToken);
            return Ok();
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
        catch (ConflictException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
    }

    /// <summary>Chronological trace tree (agent executions + tool calls) for a task run.</summary>
    [HttpGet("{id:guid}/timeline")]
    [ProducesResponseType(typeof(GetExecutionTimelineResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTimelineAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetExecutionTimelineQuery(id), cancellationToken);

        if (response is null)
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Orchestration task {id} does not exist.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(response);
    }

    /// <summary>At-a-glance summary card: status, cost, agents, retries, errors, memory/checkpoint use.</summary>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType(typeof(GetExecutionSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetExecutionSummaryQuery(id), cancellationToken);

        if (response is null)
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Orchestration task {id} does not exist.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(response);
    }

    /// <summary>Side-by-side comparison of two task runs — prompts, outputs, latency, cost, tokens.</summary>
    [HttpGet("compare")]
    [ProducesResponseType(typeof(GetTaskComparisonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompareAsync(
        [FromQuery] Guid firstTaskId, [FromQuery] Guid secondTaskId, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new GetTaskComparisonQuery(firstTaskId, secondTaskId), cancellationToken);

        if (response is null)
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"One or both tasks ({firstTaskId}, {secondTaskId}) do not exist.",
                Status = StatusCodes.Status404NotFound
            });

        return Ok(response);
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

/// <summary>Optional reviewer note attached to an approve/reject decision.</summary>
public sealed record ApprovalRequest(string? Note);
