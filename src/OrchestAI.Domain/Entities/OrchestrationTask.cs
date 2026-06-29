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
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public User User { get; private set; } = null!;
    private readonly List<AgentExecution> _agentExecutions = [];
    public IReadOnlyCollection<AgentExecution> AgentExecutions => _agentExecutions.AsReadOnly();

    public static OrchestrationTask Create(Guid userId, string title, string userPrompt)
    {
        return new OrchestrationTask
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            UserPrompt = userPrompt,
            Status = OrchestrationTaskStatus.Pending,
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

    public void MarkFailed(string error)
    {
        Status = OrchestrationTaskStatus.Failed;
        ErrorMessage = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void AccumulateCost(int inputTokens, int outputTokens, decimal costUsd)
    {
        TotalInputTokens += inputTokens;
        TotalOutputTokens += outputTokens;
        TotalCostUsd += costUsd;
    }
}
