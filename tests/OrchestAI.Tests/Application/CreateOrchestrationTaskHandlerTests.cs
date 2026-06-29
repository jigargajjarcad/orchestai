using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class CreateOrchestrationTaskHandlerTests
{
    private readonly Mock<IOrchestrationTaskRepository> _repositoryMock;
    private readonly Mock<ILogger<CreateOrchestrationTaskHandler>> _loggerMock;
    private readonly CreateOrchestrationTaskHandler _handler;

    public CreateOrchestrationTaskHandlerTests()
    {
        _repositoryMock = new Mock<IOrchestrationTaskRepository>();
        _loggerMock = new Mock<ILogger<CreateOrchestrationTaskHandler>>();
        _handler = new CreateOrchestrationTaskHandler(
            _repositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_CreatesTaskAndReturnsPendingStatus()
    {
        var userId = Guid.NewGuid();
        var command = new CreateOrchestrationTaskCommand(
            userId,
            "Analyze .NET 8 performance improvements",
            "Research and summarize the key performance improvements in .NET 8 vs .NET 7");

        _repositoryMock
            .Setup(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var response = await _handler.Handle(command, CancellationToken.None);

        response.Should().NotBeNull();
        response.Id.Should().NotBeEmpty();
        response.UserId.Should().Be(userId);
        response.Title.Should().Be(command.Title);
        response.Status.Should().Be("Pending");
        response.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));

        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_EmptyUserPrompt_ThrowsValidationException()
    {
        var command = new CreateOrchestrationTaskCommand(
            Guid.NewGuid(),
            "Some Title",
            string.Empty);

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Errors.ContainsKey(nameof(command.UserPrompt)));

        _repositoryMock.Verify(
            r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_EmptyUserId_ThrowsValidationException()
    {
        var command = new CreateOrchestrationTaskCommand(
            Guid.Empty,
            "Some Title",
            "Some prompt");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Errors.ContainsKey(nameof(command.UserId)));
    }

    [Fact]
    public async Task Handle_TitleExceeds500Chars_ThrowsValidationException()
    {
        var command = new CreateOrchestrationTaskCommand(
            Guid.NewGuid(),
            new string('A', 501),
            "Some prompt");

        var act = () => _handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .Where(ex => ex.Errors.ContainsKey(nameof(command.Title)));
    }
}
