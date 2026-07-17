namespace OrchestAI.Domain.Models;

public sealed record ReadinessResult(bool IsReady, string? Reason);
