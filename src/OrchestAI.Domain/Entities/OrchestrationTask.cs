using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class OrchestrationTask : IHasUpdatedAt
{
    private OrchestrationTask() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string UserPrompt { get; private set; } = string.Empty;
    public OrchestrationTaskStatus Status { get; private set; }
    public string? FinalResult { get; private set; }
    public int TotalInputTokens { get; private set; }
    public int TotalOutputTokens { get; private set; }
    public decimal TotalCostUsd { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool RequireApproval { get; private set; }
    public TaskApprovalStatus? ApprovalStatus { get; private set; }
    public DateTimeOffset? ApprovalRequestedAt { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? ApprovalNote { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public User User { get; private set; } = null!;
    private readonly List<AgentExecution> _agentExecutions = [];
    public IReadOnlyCollection<AgentExecution> AgentExecutions => _agentExecutions.AsReadOnly();

    public static OrchestrationTask Create(
        Guid userId, string title, string userPrompt, bool requireApproval = false)
    {
        return new OrchestrationTask
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            UserPrompt = userPrompt,
            Status = OrchestrationTaskStatus.Pending,
            RequireApproval = requireApproval,
            TotalInputTokens = 0,
            TotalOutputTokens = 0,
            TotalCostUsd = 0,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkRunning()
    {
        Status = OrchestrationTaskStatus.Running;
    }

    public void MarkCompleted(string result)
    {
        Status = OrchestrationTaskStatus.Completed;
        FinalResult = result;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error, string? finalResult = null)
    {
        Status = OrchestrationTaskStatus.Failed;
        ErrorMessage = error;
        FinalResult = finalResult;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void RequestApproval()
    {
        Status = OrchestrationTaskStatus.WaitingForApproval;
        ApprovalStatus = TaskApprovalStatus.Pending;
        ApprovalRequestedAt = DateTimeOffset.UtcNow;
    }

    public void Approve(string? note)
    {
        ApprovalStatus = TaskApprovalStatus.Approved;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovalNote = note;
        Status = OrchestrationTaskStatus.Running;
    }

    public void Reject(string? note)
    {
        ApprovalStatus = TaskApprovalStatus.Rejected;
        ApprovedAt = DateTimeOffset.UtcNow;
        ApprovalNote = note;
        MarkFailed("Task rejected by human reviewer.");
    }

    public void AccumulateCost(int inputTokens, int outputTokens, decimal costUsd)
    {
        TotalInputTokens += inputTokens;
        TotalOutputTokens += outputTokens;
        TotalCostUsd += costUsd;
    }
}
