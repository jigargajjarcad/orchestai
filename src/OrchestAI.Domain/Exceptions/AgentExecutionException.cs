namespace OrchestAI.Domain.Exceptions;

public sealed class AgentExecutionException : Exception
{
    public AgentExecutionException(string message) : base(message) { }

    public AgentExecutionException(string message, Exception innerException)
        : base(message, innerException) { }
}
