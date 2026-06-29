using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IMcpToolCallRepository
{
    Task AddAsync(McpToolCall toolCall, CancellationToken cancellationToken = default);
}
