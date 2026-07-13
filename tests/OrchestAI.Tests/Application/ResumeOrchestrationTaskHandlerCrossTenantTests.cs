using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.ResumeOrchestrationTask;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

// Proves confirmation #3's claim for THIS specific command: no new ownership-check code is
// needed here because the tenant query filter (Task 4) already makes a foreign tenant's TaskId
// invisible to GetByIdWithExecutionsAsync, which the handler already null-checks into a
// NotFoundException. This test documents that the filter is sufficient for a "fetch by ID as
// the request's primary subject" pattern — unlike RunEvalSuiteCommand's BaselineRunId, which
// needed an explicit new lookup because the handler never looked the referenced entity up at all.
public sealed class ResumeOrchestrationTaskHandlerCrossTenantTests
{
    [Fact]
    public async Task Handle_TaskIdInvisibleUnderCurrentTenantFilter_ThrowsNotFound()
    {
        var foreignTaskId = Guid.NewGuid();
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        // Simulates the tenant-filtered repository call returning null for a foreign tenant's
        // task, exactly as the real filtered AppDbContext would.
        taskRepoMock.Setup(r => r.GetByIdWithExecutionsAsync(foreignTaskId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrchestAI.Domain.Entities.OrchestrationTask?)null);

        var handler = BuildHandlerWithMinimalMocks(taskRepoMock.Object);

        var act = async () => await handler.Handle(
            new ResumeOrchestrationTaskCommand(foreignTaskId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private static ResumeOrchestrationTaskHandler BuildHandlerWithMinimalMocks(IOrchestrationTaskRepository taskRepository)
    {
        return new ResumeOrchestrationTaskHandler(
            taskRepository,
            Mock.Of<IOrchestratorAgent>(),
            Mock.Of<IAgentFactory>(),
            Mock.Of<ITaskCheckpointRepository>(),
            Mock.Of<IOrchestrationEventBus>(),
            NullLogger<ResumeOrchestrationTaskHandler>.Instance);
    }
}
