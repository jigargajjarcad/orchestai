using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.CreateOrchestrationTask;
using OrchestAI.Application.Configuration;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class CreateOrchestrationTaskIdempotencyTests
{
    private static CreateOrchestrationTaskHandler CreateHandler(
        Mock<IOrchestrationTaskRepository> taskRepoMock, Mock<IIdempotencyRecordRepository> idempotencyRepoMock)
    {
        return new CreateOrchestrationTaskHandler(
            taskRepoMock.Object,
            idempotencyRepoMock.Object,
            Options.Create(new AbuseProtectionOptions { IdempotencyKeyTtlHours = 24 }),
            NullLogger<CreateOrchestrationTaskHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NoIdempotencyKey_NeverTouchesIdempotencyRepository()
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        await handler.Handle(
            new CreateOrchestrationTaskCommand(Guid.NewGuid(), "T", "P", false, null), CancellationToken.None);

        idempotencyRepoMock.Verify(r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        idempotencyRepoMock.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_NewIdempotencyKey_CreatesTaskAndRecord()
    {
        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        idempotencyRepoMock.Setup(r => r.GetByKeyAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        var response = await handler.Handle(
            new CreateOrchestrationTaskCommand(Guid.NewGuid(), "T", "P", false, "key-1"), CancellationToken.None);

        taskRepoMock.Verify(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotencyRepoMock.Verify(r => r.AddAsync(
            It.Is<IdempotencyRecord>(rec => rec.IdempotencyKey == "key-1" && rec.TaskId == response.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RepeatedKeySamePayload_ReturnsOriginalTaskWithoutCreatingANewOne()
    {
        var originalTask = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var userId = originalTask.UserId;
        var existingRecord = IdempotencyRecord.Create("key-1", originalTask.Id, ComputeHash(userId, "T", "P", false), TimeSpan.FromHours(24));

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(originalTask.Id, It.IsAny<CancellationToken>())).ReturnsAsync(originalTask);
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        idempotencyRepoMock.Setup(r => r.GetByKeyAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync(existingRecord);
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        var response = await handler.Handle(
            new CreateOrchestrationTaskCommand(userId, "T", "P", false, "key-1"), CancellationToken.None);

        response.Id.Should().Be(originalTask.Id);
        taskRepoMock.Verify(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()), Times.Never,
            "a repeated idempotency key with the same payload must never create a second task");
        idempotencyRepoMock.Verify(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()), Times.Never,
            "a replay must never write a second idempotency record either");
    }

    [Fact]
    public async Task Handle_RepeatedKeyDifferentPayload_ThrowsConflictException()
    {
        var originalTask = OrchestrationTask.Create(Guid.NewGuid(), "T", "P", false);
        var existingRecord = IdempotencyRecord.Create(
            "key-1", originalTask.Id, ComputeHash(originalTask.UserId, "T", "P", false), TimeSpan.FromHours(24));

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        idempotencyRepoMock.Setup(r => r.GetByKeyAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync(existingRecord);
        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        var act = () => handler.Handle(
            new CreateOrchestrationTaskCommand(originalTask.UserId, "Different Title", "P", false, "key-1"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ConflictException>();
        taskRepoMock.Verify(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Review fix (Task 4): proves CreateOrchestrationTaskHandler's catch (IdempotencyKeyConflictException)
    // fallback in isolation, mocking the repository-level conflict the real-Postgres
    // IdempotencyRecordUniqueIndexIntegrationTests proves end-to-end against the actual unique
    // index. This is the "acceptable substitute" for the concurrent-race sub-case the code review
    // called out — the handler-side fallback logic doesn't need a real database to verify.
    [Fact]
    public async Task Handle_IdempotencyInsertLosesConcurrentRace_ReturnsWinningTaskInsteadOfThrowing()
    {
        var winningTask = OrchestrationTask.Create(Guid.NewGuid(), "Winning Title", "P", false);

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.GetByIdAsync(winningTask.Id, It.IsAny<CancellationToken>())).ReturnsAsync(winningTask);

        var idempotencyRepoMock = new Mock<IIdempotencyRecordRepository>();
        idempotencyRepoMock.Setup(r => r.GetByKeyAsync("key-1", It.IsAny<CancellationToken>())).ReturnsAsync((IdempotencyRecord?)null);
        idempotencyRepoMock.Setup(r => r.AddAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IdempotencyKeyConflictException(winningTask.Id));

        var handler = CreateHandler(taskRepoMock, idempotencyRepoMock);

        var response = await handler.Handle(
            new CreateOrchestrationTaskCommand(Guid.NewGuid(), "T", "P", false, "key-1"), CancellationToken.None);

        response.Id.Should().Be(winningTask.Id);
        taskRepoMock.Verify(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>()), Times.Once,
            "the loser's own task is still created — and left as a harmless orphan — before the conflict is discovered");
    }

    // Mirrors CreateOrchestrationTaskHandler.ComputeRequestHash exactly — kept in sync manually
    // since the handler's version is private; a divergence here would make this test lie.
    private static string ComputeHash(Guid userId, string title, string prompt, bool requireApproval)
    {
        var canonical = $"{userId}|{title}|{prompt}|{requireApproval}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
