namespace OrchestAI.Domain.Enums;

public enum OrchestrationTaskStatus
{
    Pending,
    Running,
    WaitingForApproval,
    Completed,
    Failed,
    Cancelled
}
