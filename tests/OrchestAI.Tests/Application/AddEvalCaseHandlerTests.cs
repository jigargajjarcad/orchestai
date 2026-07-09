using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.AddEvalCase;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class AddEvalCaseHandlerTests
{
    [Fact]
    public async Task Handle_SuiteExists_PersistsCaseUnderIt()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var repoMock = new Mock<IEvalSuiteRepository>();
        repoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        EvalCase? captured = null;
        repoMock
            .Setup(r => r.AddCaseAsync(It.IsAny<EvalCase>(), It.IsAny<CancellationToken>()))
            .Callback<EvalCase, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        var handler = new AddEvalCaseHandler(repoMock.Object, NullLogger<AddEvalCaseHandler>.Instance);

        var response = await handler.Handle(
            new AddEvalCaseCommand(
                suite.Id, "{\"prompt\":\"hi\"}", "{\"mode\":\"ExactMatch\",\"expected\":\"hi\"}",
                EvalScorerType.RuleBased, 0.05m, "smoke"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SuiteId.Should().Be(suite.Id);
        response.SuiteId.Should().Be(suite.Id);
    }

    [Fact]
    public async Task Handle_SuiteDoesNotExist_ThrowsNotFoundException()
    {
        var repoMock = new Mock<IEvalSuiteRepository>();
        repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvalSuite?)null);

        var handler = new AddEvalCaseHandler(repoMock.Object, NullLogger<AddEvalCaseHandler>.Instance);

        var act = async () => await handler.Handle(
            new AddEvalCaseCommand(Guid.NewGuid(), "{}", "{}", EvalScorerType.RuleBased, 0.05m, ""),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
