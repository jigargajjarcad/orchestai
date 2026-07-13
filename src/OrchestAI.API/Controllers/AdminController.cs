using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Commands.CreateApiKey;
using OrchestAI.Application.Commands.CreateTenant;
using OrchestAI.Application.Commands.RevokeApiKey;
using OrchestAI.Application.Commands.SuspendTenant;
using OrchestAI.Application.Exceptions;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.API.Controllers;

// Operator-only bootstrap surface — never reachable by a tenant-authenticated API key (Task 9's
// middleware only applies to /api/v1 routes OTHER than this admin prefix; see Task 9's exact
// middleware scoping). Gated by RequireAdminSecretFilter, not tenant auth.
[ApiController]
[Route("api/v1/admin")]
[ServiceFilter(typeof(RequireAdminSecretFilter))]
public sealed class AdminController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IMediator mediator, ILogger<AdminController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("tenants")]
    [ProducesResponseType(typeof(CreateTenantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateTenantAsync([FromBody] CreateTenantCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            // Literal action name, not nameof(CreateTenantAsync) — ASP.NET Core strips the
            // "Async" suffix from routed action names by default (SuppressAsyncSuffixInActionNames),
            // so nameof(CreateTenantAsync) never matches and CreatedAtAction throws
            // InvalidOperationException at runtime while the tenant is already persisted. See
            // commit 4c5f34b (EvalsController hit the same bug).
            return CreatedAtAction("CreateTenant", response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for CreateTenant: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    [HttpPost("api-keys")]
    [ProducesResponseType(typeof(CreateApiKeyResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateApiKeyAsync([FromBody] CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            // See CreateTenantAsync above — literal action name, not nameof(CreateApiKeyAsync).
            return CreatedAtAction("CreateApiKey", response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for CreateApiKey: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    [HttpPost("api-keys/{apiKeyId:guid}/revoke")]
    [ProducesResponseType(typeof(RevokeApiKeyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeApiKeyAsync(Guid apiKeyId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new RevokeApiKeyCommand(apiKeyId), cancellationToken);
            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
    }

    [HttpPost("tenants/{tenantId:guid}/suspend")]
    [ProducesResponseType(typeof(SuspendTenantResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new SuspendTenantCommand(tenantId), cancellationToken);
            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails { Title = "Not Found", Detail = ex.Message, Status = StatusCodes.Status404NotFound });
        }
    }
}
