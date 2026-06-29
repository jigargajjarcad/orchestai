using FluentAssertions;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Agents;

namespace OrchestAI.Tests.Infrastructure;

public sealed class OrchestratorAgentTests
{
    [Fact]
    public void TryParseOrchestrationPlan_ValidJson_ReturnsTrueWithCorrectPlan()
    {
        var json = """
            {
              "plan": "Research the topic then write a report",
              "agents": ["Research", "Writer"],
              "reasoning": {
                "Research": "Need to gather information",
                "Writer": "Need to produce the final content"
              },
              "agent_prompts": {
                "Research": "Research .NET 8 performance improvements thoroughly",
                "Writer": "Write a professional report based on the research findings"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan.Should().NotBeNull();
        plan!.Plan.Should().Be("Research the topic then write a report");
        plan.SelectedAgents.Should().HaveCount(2);
        plan.SelectedAgents.Should().Contain(AgentType.Research);
        plan.SelectedAgents.Should().Contain(AgentType.Writer);
        plan.AgentPrompts.Should().ContainKey(AgentType.Research);
        plan.AgentPrompts[AgentType.Research].Should().Contain(".NET 8");
        plan.AgentPrompts.Should().ContainKey(AgentType.Writer);
    }

    [Fact]
    public void TryParseOrchestrationPlan_SingleAgent_ParsesCorrectly()
    {
        var json = """
            {
              "plan": "Use only the code agent",
              "agents": ["Code"],
              "agent_prompts": {
                "Code": "Generate a C# async method that calls an HTTP API"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.SelectedAgents.Should().HaveCount(1);
        plan.SelectedAgents[0].Should().Be(AgentType.Code);
    }

    [Fact]
    public void TryParseOrchestrationPlan_JsonWrappedInMarkdownFence_ParsesCorrectly()
    {
        var json = """
            ```json
            {
              "plan": "Use research agent",
              "agents": ["Research"],
              "agent_prompts": {
                "Research": "Research the topic"
              }
            }
            ```
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.SelectedAgents.Should().Contain(AgentType.Research);
    }

    [Fact]
    public void TryParseOrchestrationPlan_InvalidJson_ReturnsFalse()
    {
        var json = "This is not JSON at all, just a plain text response.";

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeFalse();
        plan.Should().BeNull();
    }

    [Fact]
    public void TryParseOrchestrationPlan_ValidJsonMissingAgentsField_ReturnsFalse()
    {
        var json = """{ "plan": "do something" }""";

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseOrchestrationPlan_UnknownAgentName_IgnoresUnknown()
    {
        var json = """
            {
              "plan": "Use known and unknown agents",
              "agents": ["Research", "UnknownAgent"],
              "agent_prompts": {
                "Research": "Research something"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.SelectedAgents.Should().HaveCount(1);
        plan.SelectedAgents.Should().Contain(AgentType.Research);
    }

    [Fact]
    public void TryParseOrchestrationPlan_CaseInsensitiveAgentNames_ParsesCorrectly()
    {
        var json = """
            {
              "plan": "Use data agent",
              "agents": ["data"],
              "agent_prompts": {
                "data": "Analyze the dataset"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.SelectedAgents.Should().Contain(AgentType.Data);
    }

    [Fact]
    public void TryParseOrchestrationPlan_WithSequentialMode_ParsesExecutionMode()
    {
        var json = """
            {
              "plan": "Research then write sequentially",
              "execution_mode": "sequential",
              "agents": ["Research", "Writer"],
              "execution_order": ["Research", "Writer"],
              "agent_prompts": {
                "Research": "Research .NET AI libraries",
                "Writer": "Write a report based on findings"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.ExecutionMode.Should().Be(ExecutionMode.Sequential);
    }

    [Fact]
    public void TryParseOrchestrationPlan_WithExplicitExecutionOrder_ParsesOrder()
    {
        var json = """
            {
              "plan": "Data first, then write report",
              "execution_mode": "sequential",
              "agents": ["Data", "Writer"],
              "execution_order": ["Data", "Writer"],
              "agent_prompts": {
                "Data": "Extract structured data",
                "Writer": "Format the data into a report"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.ExecutionOrder.Should().HaveCount(2);
        plan.ExecutionOrder[0].Should().Be(AgentType.Data);
        plan.ExecutionOrder[1].Should().Be(AgentType.Writer);
    }

    [Fact]
    public void TryParseOrchestrationPlan_MissingExecutionOrder_DefaultsToAgentsOrder()
    {
        var json = """
            {
              "plan": "Use two agents",
              "execution_mode": "sequential",
              "agents": ["Research", "Code"],
              "agent_prompts": {
                "Research": "Research the topic",
                "Code": "Write the code"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.ExecutionOrder.Should().HaveCount(2);
        plan.ExecutionOrder[0].Should().Be(AgentType.Research);
        plan.ExecutionOrder[1].Should().Be(AgentType.Code);
    }

    [Fact]
    public void TryParseOrchestrationPlan_MissingExecutionMode_DefaultsToParallel()
    {
        var json = """
            {
              "plan": "Run agents in parallel",
              "agents": ["Research", "Data"],
              "agent_prompts": {
                "Research": "Research topic",
                "Data": "Analyze data"
              }
            }
            """;

        var result = OrchestratorAgent.TryParseOrchestrationPlan(json, out var plan);

        result.Should().BeTrue();
        plan!.ExecutionMode.Should().Be(ExecutionMode.Parallel);
    }
}
