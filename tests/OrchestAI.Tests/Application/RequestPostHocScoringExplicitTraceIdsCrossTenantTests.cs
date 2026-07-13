using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Application.Configuration;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Tests.Application;

// Proves confirmation #3's claim for explicit TraceIds: a cross-tenant ID in the list is
// silently excluded by the (mocked-here, real-in-production) tenant-filtered
// SelectForPostHocScoringAsync query — it does not throw, it just isn't in the resolved set.
// This is deliberately the SAME behavior as a date-range selection already has (silently
// scoped to what's visible), not a new explicit-rejection code path.
public sealed class RequestPostHocScoringExplicitTraceIdsCrossTenantTests
{
    [Fact]
    public async Task Handle_ExplicitTraceIdsIncludingForeignTenantId_ForeignIdSilentlyExcluded()
    {
        var ownTraceId = Guid.NewGuid();
        var foreignTraceId = Guid.NewGuid();

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        // Simulates the tenant-filtered repository call: the foreign trace ID was in the
        // request, but the (real, filtered) query only ever resolves the caller's own trace —
        // TotalMatched reflects only what's actually visible.
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                null, null, null,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(ownTraceId) && ids.Contains(foreignTraceId)),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult([ownTraceId], TotalMatched: 1));

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.AddAsync(It.IsAny<OrchestAI.Domain.Entities.EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var options = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });

        var handler = new RequestPostHocScoringHandler(
            executionRepoMock.Object, runRepoMock.Object, queueMock.Object, options,
            NullLogger<RequestPostHocScoringHandler>.Instance);

        var command = new RequestPostHocScoringCommand(
            DateFrom: null, DateTo: null, AgentType: null, TraceIds: [ownTraceId, foreignTraceId],
            ScorerType: EvalScorerType.LlmJudge, Rubric: "was it appropriate?", PassThreshold: null, MaxTraces: 10);

        var response = await handler.Handle(command, CancellationToken.None);

        response.ResolvedTraceCount.Should().Be(1, "the foreign-tenant trace ID must be silently excluded, not rejected as an error");
    }
}
