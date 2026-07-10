namespace OrchestAI.Domain.Models;

public sealed record TraceSelectionResult(IReadOnlyList<Guid> AgentExecutionIds, int TotalMatched);
