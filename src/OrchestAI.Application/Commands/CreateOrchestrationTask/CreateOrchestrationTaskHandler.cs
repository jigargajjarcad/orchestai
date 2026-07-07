using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateOrchestrationTask;

public sealed class CreateOrchestrationTaskHandler
    : IRequestHandler<CreateOrchestrationTaskCommand, CreateOrchestrationTaskResponse>
{
    private readonly IOrchestrationTaskRepository _repository;
    private readonly ILogger<CreateOrchestrationTaskHandler> _logger;

    public CreateOrchestrationTaskHandler(
        IOrchestrationTaskRepository repository,
        ILogger<CreateOrchestrationTaskHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<CreateOrchestrationTaskResponse> Handle(
        CreateOrchestrationTaskCommand request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var task = OrchestrationTask.Create(
            request.UserId,
            request.Title,
            request.UserPrompt,
            request.RequireApproval);

        await _repository.AddAsync(task, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Created orchestration task {TaskId} for user {UserId}",
            task.Id, task.UserId);

        return new CreateOrchestrationTaskResponse(
            task.Id,
            task.UserId,
            task.Title,
            task.Status.ToString(),
            task.RequireApproval,
            task.CreatedAt);
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
