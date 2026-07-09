using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Commands.AddEvalCase;
using OrchestAI.Application.Commands.CreateEvalSuite;
using OrchestAI.Application.Commands.RunEvalSuite;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetEvalRunResults;
using OrchestAI.Application.Queries.GetEvalRuns;
using OrchestAI.Application.Queries.GetEvalSuites;
using OrchestAI.Application.Queries.GetRegressionReport;
using OrchestAI.Domain.Enums;

namespace OrchestAI.API.Controllers;

[ApiController]
[Route("api/v1/eval-suites")]
[Produces("application/json")]
public sealed class EvalsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<EvalsController> _logger;

    public EvalsController(IMediator mediator, ILogger<EvalsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public sealed record AddEvalCaseRequest(
        JsonElement InputPayload, JsonElement ExpectedCriteria, EvalScorerType ScorerType,
        decimal RegressionThreshold, string Tags = "");

    public sealed record TriggerEvalRunRequest(string SubjectVersion, Guid? BaselineRunId);

    /// <summary>Creates a new eval suite targeting one agent type.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateEvalSuiteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSuiteAsync(
        [FromBody] CreateEvalSuiteCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction("GetSuites", null, response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for CreateEvalSuite: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Lists all eval suites.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetEvalSuitesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuitesAsync(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetEvalSuitesQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>Adds a test case to an existing suite.</summary>
    [HttpPost("{suiteId:guid}/cases")]
    [ProducesResponseType(typeof(AddEvalCaseResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddCaseAsync(
        Guid suiteId, [FromBody] AddEvalCaseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var command = new AddEvalCaseCommand(
                suiteId, request.InputPayload.GetRawText(), request.ExpectedCriteria.GetRawText(),
                request.ScorerType, request.RegressionThreshold, request.Tags);
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction("GetSuites", null, response);
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
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for AddEvalCase: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Triggers a run of a suite, optionally against a baseline run for comparison.</summary>
    [HttpPost("{suiteId:guid}/runs")]
    [ProducesResponseType(typeof(RunEvalSuiteResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TriggerRunAsync(
        Guid suiteId, [FromBody] TriggerEvalRunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(
                new RunEvalSuiteCommand(suiteId, request.SubjectVersion, request.BaselineRunId), cancellationToken);
            return AcceptedAtAction("GetRunResults", new { runId = response.EvalRunId }, response);
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
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for RunEvalSuite: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Lists runs for a suite, newest first — powers the baseline-run picker.</summary>
    [HttpGet("{suiteId:guid}/runs")]
    [ProducesResponseType(typeof(GetEvalRunsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRunsAsync(Guid suiteId, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetEvalRunsQuery(suiteId), cancellationToken);
        return Ok(response);
    }

    /// <summary>Gets per-case results for one eval run.</summary>
    [HttpGet("/api/v1/eval-runs/{runId:guid}/results")]
    [ProducesResponseType(typeof(GetEvalRunResultsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRunResultsAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new GetEvalRunResultsQuery(runId), cancellationToken);
            return Ok(response);
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

    /// <summary>Diffs a run against its explicit baseline run — 400s if no baseline was set.</summary>
    [HttpGet("/api/v1/eval-runs/{runId:guid}/regression-report")]
    [ProducesResponseType(typeof(GetRegressionReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRegressionReportAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new GetRegressionReportQuery(runId), cancellationToken);
            return Ok(response);
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
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for GetRegressionReport: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }
}
