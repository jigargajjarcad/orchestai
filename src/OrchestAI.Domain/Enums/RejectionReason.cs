namespace OrchestAI.Domain.Enums;

public enum RejectionReason
{
    RateLimited,
    ConcurrencyExceeded,
    BudgetExceeded,
    AgentCapExceeded,
    QueueBackpressure
}
