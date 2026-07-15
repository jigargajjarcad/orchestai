using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateOrchestrationTask;

public sealed class CreateOrchestrationTaskHandler
    : IRequestHandler<CreateOrchestrationTaskCommand, CreateOrchestrationTaskResponse>
{
    private readonly IOrchestrationTaskRepository _repository;
    private readonly IIdempotencyRecordRepository _idempotencyRepository;
    private readonly IOptions<AbuseProtectionOptions> _abuseProtectionOptions;
    private readonly ILogger<CreateOrchestrationTaskHandler> _logger;

    public CreateOrchestrationTaskHandler(
        IOrchestrationTaskRepository repository,
        IIdempotencyRecordRepository idempotencyRepository,
        IOptions<AbuseProtectionOptions> abuseProtectionOptions,
        ILogger<CreateOrchestrationTaskHandler> logger)
    {
        _repository = repository;
        _idempotencyRepository = idempotencyRepository;
        _abuseProtectionOptions = abuseProtectionOptions;
        _logger = logger;
    }

    public async Task<CreateOrchestrationTaskResponse> Handle(
        CreateOrchestrationTaskCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var requestHash = ComputeRequestHash(request);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _idempotencyRepository
                .GetByKeyAsync(request.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
            {
                if (existing.RequestPayloadHash != requestHash)
                    throw new ConflictException(
                        $"Idempotency-Key '{request.IdempotencyKey}' was already used with a different request payload.");

                var originalTask = await _repository.GetByIdAsync(existing.TaskId, cancellationToken).ConfigureAwait(false)
                    ?? throw new NotFoundException(nameof(OrchestrationTask), existing.TaskId);

                _logger.LogInformation(
                    "Idempotency-Key {IdempotencyKey} matched an existing task {TaskId} — returning it unchanged",
                    request.IdempotencyKey, originalTask.Id);

                return ToResponse(originalTask);
            }
        }

        var task = OrchestrationTask.Create(
            request.UserId,
            request.Title,
            request.UserPrompt,
            request.RequireApproval);

        await _repository.AddAsync(task, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var record = IdempotencyRecord.Create(
                request.IdempotencyKey, task.Id, requestHash,
                TimeSpan.FromHours(_abuseProtectionOptions.Value.IdempotencyKeyTtlHours));
            await _idempotencyRepository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Created orchestration task {TaskId} for user {UserId}",
            task.Id, task.UserId);

        return ToResponse(task);
    }

    private static CreateOrchestrationTaskResponse ToResponse(OrchestrationTask task) => new(
        task.Id, task.UserId, task.Title, task.Status.ToString(), task.RequireApproval, task.CreatedAt);

    // Deliberately excludes IdempotencyKey itself from the hash — the key is the lookup handle,
    // not part of "what was requested." Kept in sync manually with the test-side copy in
    // CreateOrchestrationTaskIdempotencyTests — a divergence there would make that test lie.
    private static string ComputeRequestHash(CreateOrchestrationTaskCommand request)
    {
        var canonical = $"{request.UserId}|{request.Title}|{request.UserPrompt}|{request.RequireApproval}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static void Validate(CreateOrchestrationTaskCommand request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.UserId == Guid.Empty)
            errors[nameof(request.UserId)] = ["UserId must not be empty."];

        if (string.IsNullOrWhiteSpace(request.Title))
            errors[nameof(request.Title)] = ["Title is required."];
        else if (request.Title.Length > 500)
            errors[nameof(request.Title)] = ["Title must not exceed 500 characters."];

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
            errors[nameof(request.UserPrompt)] = ["UserPrompt is required."];

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }
}
