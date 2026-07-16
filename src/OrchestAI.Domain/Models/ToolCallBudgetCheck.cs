namespace OrchestAI.Domain.Models;

public sealed record ToolCallBudgetCheck(bool Allowed, int CurrentCount, int MaxToolCalls);
