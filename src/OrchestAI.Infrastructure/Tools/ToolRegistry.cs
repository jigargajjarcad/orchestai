using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, IMcpTool> _tools;

    public ToolRegistry(IEnumerable<IMcpTool> tools)
        => _tools = tools.ToDictionary(t => t.ToolName, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IMcpTool> GetTools(IReadOnlyList<string> toolNames)
        => toolNames
            .Where(n => _tools.ContainsKey(n))
            .Select(n => _tools[n])
            .ToList();

    public IMcpTool Get(string toolName)
        => _tools.TryGetValue(toolName, out var tool)
            ? tool
            : throw new KeyNotFoundException($"Tool '{toolName}' is not registered.");
}
