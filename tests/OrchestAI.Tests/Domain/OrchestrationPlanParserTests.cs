using FluentAssertions;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Domain;

// Guards the fix for the live P2-M4 crash: a real orchestrator plan named an agent ("Writer")
// in agents/execution_order with no corresponding agent_prompts entry. The dispatch path looks
// up agent_prompts[agentType] directly, so that inconsistency threw a KeyNotFoundException
// mid-orchestration. The parser now drops any agent without a prompt so an inconsistent plan
// can never reach dispatch in a crashing shape.
public sealed class OrchestrationPlanParserTests
{
    [Fact]
    public void TryParse_ConsistentPlan_KeepsEveryAgent()
    {
        const string json = """
        {
          "plan": "p",
          "execution_mode": "parallel",
          "agents": ["Research", "Writer"],
          "execution_order": ["Research", "Writer"],
          "agent_prompts": { "Research": "r", "Writer": "w" }
        }
        """;

        OrchestrationPlanParser.TryParse(json, out var plan).Should().BeTrue();
        plan!.ExecutionOrder.Should().Equal(AgentType.Research, AgentType.Writer);
        plan.ExecutionOrder.Should().OnlyContain(a => plan.AgentPrompts.ContainsKey(a));
    }

    [Fact]
    public void TryParse_AgentNamedWithoutPrompt_DropsThatAgent_ReproducesWriterBug()
    {
        // The exact live failure: Writer in agents/execution_order, absent from agent_prompts.
        const string json = """
        {
          "plan": "p",
          "execution_mode": "sequential",
          "agents": ["Data", "Research", "Writer"],
          "execution_order": ["Data", "Research", "Writer"],
          "agent_prompts": { "Data": "d", "Research": "r" }
        }
        """;

        OrchestrationPlanParser.TryParse(json, out var plan).Should().BeTrue();

        plan!.ExecutionOrder.Should().Equal(AgentType.Data, AgentType.Research);
        plan.ExecutionOrder.Should().NotContain(AgentType.Writer);
        plan.SelectedAgents.Should().NotContain(AgentType.Writer);
        // Post-condition that makes the dispatch KeyNotFoundException structurally impossible:
        plan.ExecutionOrder.Should().OnlyContain(a => plan.AgentPrompts.ContainsKey(a));
    }

    [Fact]
    public void TryParse_NoAgentHasAPrompt_RejectsPlanSoCallerCanRetryOrFail()
    {
        const string json = """
        {
          "plan": "p",
          "execution_mode": "parallel",
          "agents": ["Writer"],
          "execution_order": ["Writer"],
          "agent_prompts": { }
        }
        """;

        OrchestrationPlanParser.TryParse(json, out var plan).Should().BeFalse();
        plan.Should().BeNull();
    }
}
