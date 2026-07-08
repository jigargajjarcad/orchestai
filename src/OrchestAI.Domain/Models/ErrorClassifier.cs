using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Exceptions;

namespace OrchestAI.Domain.Models;

// Classifies failures at the point they're caught (where the real exception type is known)
// rather than reconstructing a category from a stored free-text message later.
public static class ErrorClassifier
{
    public static ExecutionErrorCategory Classify(Exception ex) => ex switch
    {
        TimeoutException => ExecutionErrorCategory.Timeout,
        TaskCanceledException or OperationCanceledException => ExecutionErrorCategory.Timeout,
        AgentExecutionException => ExecutionErrorCategory.ProviderError,
        HttpRequestException => ExecutionErrorCategory.ProviderError,
        ArgumentException or FormatException => ExecutionErrorCategory.ValidationFailure,
        _ when ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase) => ExecutionErrorCategory.ProviderError,
        _ when ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => ExecutionErrorCategory.Timeout,
        _ => ExecutionErrorCategory.Unknown
    };
}
