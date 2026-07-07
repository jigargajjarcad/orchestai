namespace OrchestAI.Infrastructure.Configuration;

public sealed class RetryPolicyOptions
{
    public const string SectionName = "RetryPolicy";

    public int MaxAttempts { get; init; } = 3;
    public int InitialDelayMs { get; init; } = 1000;
    public int MaxDelayMs { get; init; } = 30000;
    public double BackoffMultiplier { get; init; } = 2.0;
    public int JitterMs { get; init; } = 500;
}
