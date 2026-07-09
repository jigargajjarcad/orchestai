using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.CreateEvalSuite;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class CreateEvalSuiteHandlerTests
{
    [Fact]
    public async Task Handle_ValidRequest_PersistsSuiteAndReturnsResponse()
    {
        var repoMock = new Mock<IEvalSuiteRepository>();
        EvalSuite? captured = null;
        repoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalSuite>(), It.IsAny<CancellationToken>()))
            .Callback<EvalSuite, CancellationToken>((s, _) => captured = s)
            .Returns(Task.CompletedTask);

        var handler = new CreateEvalSuiteHandler(repoMock.Object, NullLogger<CreateEvalSuiteHandler>.Instance);

        var response = await handler.Handle(
            new CreateEvalSuiteCommand("Research smoke suite", "Basic research agent checks", AgentType.Research),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Research smoke suite");
        response.TargetAgentType.Should().Be("Research");
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsValidationException()
    {
        var repoMock = new Mock<IEvalSuiteRepository>();
        var handler = new CreateEvalSuiteHandler(repoMock.Object, NullLogger<CreateEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new CreateEvalSuiteCommand("", "desc", AgentType.Research), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
