using System.Text.Json;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Models;

public static class OrchestrationPlanParser
{
    public static bool TryParse(string json, out OrchestrationPlan? plan)
    {
        plan = null;
        try
        {
            var cleaned = json.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                var start = cleaned.IndexOf('\n') + 1;
                var end = cleaned.LastIndexOf("```", StringComparison.Ordinal);
                if (end > start) cleaned = cleaned[start..end].Trim();
            }

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var planText = root.GetProperty("plan").GetString() ?? string.Empty;

            var executionMode = ExecutionMode.Parallel;
            if (root.TryGetProperty("execution_mode", out var modeEl))
            {
                var modeStr = modeEl.GetString();
                if (Enum.TryParse<ExecutionMode>(modeStr, ignoreCase: true, out var parsedMode))
                    executionMode = parsedMode;
            }

            var selectedAgents = new List<AgentType>();
            foreach (var agentEl in root.GetProperty("agents").EnumerateArray())
            {
                var agentStr = agentEl.GetString();
                if (Enum.TryParse<AgentType>(agentStr, ignoreCase: true, out var agentType))
                    selectedAgents.Add(agentType);
            }

            var executionOrder = new List<AgentType>();
            if (root.TryGetProperty("execution_order", out var orderEl))
            {
                foreach (var orderAgentEl in orderEl.EnumerateArray())
                {
                    var agentStr = orderAgentEl.GetString();
                    if (Enum.TryParse<AgentType>(agentStr, ignoreCase: true, out var agentType)
                        && selectedAgents.Contains(agentType))
                        executionOrder.Add(agentType);
                }
            }

            if (executionOrder.Count == 0)
                executionOrder.AddRange(selectedAgents);

            var agentPrompts = new Dictionary<AgentType, string>();
            if (root.TryGetProperty("agent_prompts", out var promptsEl))
            {
                foreach (var item in promptsEl.EnumerateObject())
                {
                    if (Enum.TryParse<AgentType>(item.Name, ignoreCase: true, out var agentType))
                        agentPrompts[agentType] = item.Value.GetString() ?? string.Empty;
                }
            }

            plan = new OrchestrationPlan(planText, executionMode, selectedAgents, executionOrder, agentPrompts, null!);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
