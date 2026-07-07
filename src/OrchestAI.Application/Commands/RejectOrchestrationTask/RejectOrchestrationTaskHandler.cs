using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.RejectOrchestrationTask;

public sealed class RejectOrchestrationTaskHandler : IRequestHandler<RejectOrchestrationTaskCommand, Unit>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly IApprovalGateway _approvalGateway;
    private readonly IOrchestrationEventBus _eventBus;
    private readonly ILogger<RejectOrchestrationTaskHandler> _logger;

    public RejectOrchestrationTaskHandler(
        IOrchestrationTaskRepository taskRepository,
        IApprovalGateway approvalGateway,
        IOrchestrationEventBus eventBus,
        ILogger<RejectOrchestrationTaskHandler> logger)
    {
        _taskRepository = taskRepository;
        _approvalGateway = approvalGateway;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Unit> Handle(RejectOrchestrationTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

        if (task.Status != OrchestrationTaskStatus.WaitingForApproval)
            throw new ConflictException(
                $"Task {request.TaskId} is in '{task.Status}' state and is not awaiting approval.");

        task.Reject(request.Note);
        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(request.TaskId, new SseEvent(
            "task_rejected",
            request.TaskId,
            new { taskId = request.TaskId, note = request.Note },
            DateTimeOffset.UtcNow));

        _eventBus.Publish(request.TaskId, new SseEvent(
            "task_failed",
            request.TaskId,
            new { taskId = request.TaskId, errorMessage = task.ErrorMessage },
            DateTimeOffset.UtcNow));

        _approvalGateway.Signal(request.TaskId, approved: false);

        _logger.LogInformation("Task {TaskId} rejected by reviewer", request.TaskId);

        return Unit.Value;
    }
}
