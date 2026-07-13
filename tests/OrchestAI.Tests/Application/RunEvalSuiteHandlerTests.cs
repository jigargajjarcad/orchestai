using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.RunEvalSuite;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class RunEvalSuiteHandlerTests
{
    [Fact]
    public async Task Handle_SuiteExists_CreatesPendingRunAndEnqueuesIt()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        EvalRun? captured = null;
        runRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()))
            .Callback<EvalRun, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RunEvalSuiteHandler(
            suiteRepoMock.Object, runRepoMock.Object, queueMock.Object, NullLogger<RunEvalSuiteHandler>.Instance);

        var response = await handler.Handle(
            new RunEvalSuiteCommand(suite.Id, "commit-abc123", BaselineRunId: null), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Status.Should().Be(EvalRunStatus.Pending);
        captured.SubjectVersion.Should().Be("commit-abc123");
        response.EvalRunId.Should().Be(captured.Id);
        queueMock.Verify(q => q.EnqueueAsync(captured.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SuiteDoesNotExist_ThrowsNotFoundException()
    {
        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvalSuite?)null);

        var handler = new RunEvalSuiteHandler(
            suiteRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>(),
            NullLogger<RunEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new RunEvalSuiteCommand(Guid.NewGuid(), "v1", null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_EmptySubjectVersion_ThrowsValidationException()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var handler = new RunEvalSuiteHandler(
            suiteRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>(),
            NullLogger<RunEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new RunEvalSuiteCommand(suite.Id, "", null), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_BaselineRunIdBelongingToAnotherTenant_ThrowsNotFound()
    {
        // Simulates the tenant-filtered repository's real behavior: a foreign-tenant
        // BaselineRunId resolves to null via GetByIdAsync, exactly as it would once the global
        // query filter (Task 4) is live against a real AppDbContext scoped to a different tenant.
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var foreignBaselineRunId = Guid.NewGuid();

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(foreignBaselineRunId, It.IsAny<CancellationToken>())).ReturnsAsync((EvalRun?)null);

        var handler = new RunEvalSuiteHandler(suiteRepoMock.Object, runRepoMock.Object, Mock.Of<IEvalRunQueue>(), NullLogger<RunEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new RunEvalSuiteCommand(suite.Id, "v1", foreignBaselineRunId), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
        runRepoMock.Verify(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()), Times.Never,
            "no EvalRun should be created when the requested baseline can't be verified");
    }
}
