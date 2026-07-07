namespace OrchestAI.Domain.Interfaces;

public interface IApprovalGateway
{
    /// <summary>Called by StartOrchestrationHandler to block until approved/rejected.</summary>
    Task WaitForApprovalAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Called by the approve/reject command handlers to unblock the waiting execution.</summary>
    void Signal(Guid taskId, bool approved);
}
