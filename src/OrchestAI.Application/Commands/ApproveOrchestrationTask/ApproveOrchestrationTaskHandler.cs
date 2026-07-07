using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.ApproveOrchestrationTask;

public sealed class ApproveOrchestrationTaskHandler : IRequestHandler<ApproveOrchestrationTaskCommand, Unit>
{
    private readonly IOrchestrationTaskRepository _taskRepository;
    private readonly IApprovalGateway _approvalGateway;
    private readonly IOrchestrationEventBus _eventBus;
    private readonly ILogger<ApproveOrchestrationTaskHandler> _logger;

    public ApproveOrchestrationTaskHandler(
        IOrchestrationTaskRepository taskRepository,
        IApprovalGateway approvalGateway,
        IOrchestrationEventBus eventBus,
        ILogger<ApproveOrchestrationTaskHandler> logger)
    {
        _taskRepository = taskRepository;
        _approvalGateway = approvalGateway;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Unit> Handle(ApproveOrchestrationTaskCommand request, CancellationToken cancellationToken)
    {
        var task = await _taskRepository.GetByIdAsync(request.TaskId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(OrchestrationTask), request.TaskId);

        if (task.Status != OrchestrationTaskStatus.WaitingForApproval)
            throw new ConflictException(
                $"Task {request.TaskId} is in '{task.Status}' state and is not awaiting approval.");

        task.Approve(request.Note);
        await _taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        _eventBus.Publish(request.TaskId, new SseEvent(
            "task_approved",
            request.TaskId,
            new { taskId = request.TaskId, note = request.Note },
            DateTimeOffset.UtcNow));

        _approvalGateway.Signal(request.TaskId, approved: true);

        _logger.LogInformation("Task {TaskId} approved by reviewer", request.TaskId);

        return Unit.Value;
    }
}
