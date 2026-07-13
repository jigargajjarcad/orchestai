using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Application.Configuration;

namespace OrchestAI.Tests.Application;

public sealed class RequestPostHocScoringHandlerTests
{
    private static RequestPostHocScoringHandler BuildHandler(
        IAgentExecutionRepository executionRepo, IEvalRunRepository runRepo, IEvalRunQueue queue)
    {
        var options = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });
        return new RequestPostHocScoringHandler(
            executionRepo, runRepo, queue, options, NullLogger<RequestPostHocScoringHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NoDateRangeAndNoTraceIds_ThrowsValidation_AgentTypeAloneDoesNotBoundSelection()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateFrom: null, DateTo: null, AgentType: AgentType.Research, TraceIds: null,
            ScorerType: EvalScorerType.LlmJudge, Rubric: "was it appropriate?", PassThreshold: null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_RuleBasedScorerType_ThrowsValidation_PostHocIsJudgeOnly()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateFrom: DateTimeOffset.UtcNow.AddDays(-7), DateTo: DateTimeOffset.UtcNow, AgentType: null,
            TraceIds: null, ScorerType: EvalScorerType.RuleBased, Rubric: "n/a", PassThreshold: null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_EmptyRubric_ThrowsValidation()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateFrom: DateTimeOffset.UtcNow.AddDays(-7), DateTo: DateTimeOffset.UtcNow, AgentType: null,
            TraceIds: null, ScorerType: EvalScorerType.LlmJudge, Rubric: "   ", PassThreshold: null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_MaxTracesExceedsCeiling_ThrowsValidation()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "rubric", null, MaxTraces: 5000);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_SelectionExceedsMaxTraces_ThrowsValidationInsteadOfSilentlyTruncating()
    {
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult([Guid.NewGuid(), Guid.NewGuid()], TotalMatched: 250));

        var handler = BuildHandler(executionRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "was it appropriate?", null, MaxTraces: 2);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_NoMatchingTraces_ThrowsValidation()
    {
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult([], TotalMatched: 0));

        var handler = BuildHandler(executionRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "was it appropriate?", null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_ValidBoundedSelection_CreatesPostHocRunAndEnqueues()
    {
        var resolvedIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult(resolvedIds, TotalMatched: 2));

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = BuildHandler(executionRepoMock.Object, runRepoMock.Object, queueMock.Object);

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, AgentType.Research, null,
            EvalScorerType.LlmJudge, "was it appropriate?", 0.7m, MaxTraces: 100);

        var response = await handler.Handle(command, CancellationToken.None);

        response.ResolvedTraceCount.Should().Be(2);
        response.Status.Should().Be("Pending");
        runRepoMock.Verify(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()), Times.Once);
        queueMock.Verify(q => q.EnqueueAsync(response.EvalRunId, It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ForceRescoreTrue_ThreadsFlagOntoCreatedRun()
    {
        var resolvedIds = new List<Guid> { Guid.NewGuid() };
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult(resolvedIds, TotalMatched: 1));

        EvalRun? captured = null;
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()))
            .Callback<EvalRun, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = BuildHandler(executionRepoMock.Object, runRepoMock.Object, queueMock.Object);

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "was it appropriate?", null, MaxTraces: 100, ForceRescore: true);

        await handler.Handle(command, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ForceRescore.Should().BeTrue();
    }
}
