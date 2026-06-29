namespace OrchestAI.Domain.Interfaces;

public interface IToolRegistry
{
    IReadOnlyList<IMcpTool> GetTools(IReadOnlyList<string> toolNames);
    IMcpTool Get(string toolName);
}
