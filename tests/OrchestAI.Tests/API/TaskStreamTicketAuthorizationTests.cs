using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.API.Controllers;
using OrchestAI.API.Middleware;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Events;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.API;

// Covers Task 1 (Phase 1 architecture/product validation): EventSource cannot send an
// Authorization header, so GET {id}/stream must be exempted from TenantAuthenticationMiddleware
// and instead validate a short-lived (tenantId, taskId)-bound ticket itself. See
// ITaskStreamTicketIssuer and TenantAuthenticationMiddlewareTests.cs (the pattern this follows).
public sealed class TaskStreamTicketAuthorizationTests
{
    // Records the ambient tenant it observes during SubscribeAsync so tests can prove
    // StreamAsync's `using (_tenantAccessor.SetTenant(tenantId))` actually took effect for the
    // downstream event-bus subscription, not just for the ticket check itself.
    private sealed class RecordingEventBus : IOrchestrationEventBus
    {
        private readonly ICurrentTenantAccessor _accessor;

        public RecordingEventBus(ICurrentTenantAccessor accessor) => _accessor = accessor;

        public Guid? LastSubscribedTaskId { get; private set; }
        public Guid? ObservedTenantIdDuringSubscription { get; private set; }
        public bool SubscribeAsyncWasCalled { get; private set; }

        public void Publish(Guid taskId, SseEvent sseEvent) => throw new NotSupportedException();

        public async IAsyncEnumerable<SseEvent> SubscribeAsync(
            Guid taskId, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            SubscribeAsyncWasCalled = true;
            LastSubscribedTaskId = taskId;
            ObservedTenantIdDuringSubscription = _accessor.TenantId;
            await Task.CompletedTask;
            yield break; // empty stream — enough to prove the handler was reached without hanging
        }
    }

    private static TasksController BuildController(
        ITaskStreamTicketIssuer ticketIssuer, ICurrentTenantAccessor accessor, out RecordingEventBus eventBus)
    {
        eventBus = new RecordingEventBus(accessor);
        return new TasksController(
            Mock.Of<MediatR.IMediator>(),
            eventBus,
            ticketIssuer,
            accessor,
            NullLogger<TasksController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new DefaultHttpContext { Response = { Body = new MemoryStream() } }
            }
        };
    }

    [Fact]
    public async Task StreamAsync_NoTicket_Returns401_WithoutTouchingEventBus()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var ticketIssuer = new Mock<ITaskStreamTicketIssuer>();
        Guid outTenantId;
        ticketIssuer.Setup(i => i.TryConsume(It.IsAny<string?>(), It.IsAny<Guid>(), out outTenantId))
            .Returns(false);
        var controller = BuildController(ticketIssuer.Object, accessor, out var eventBus);

        await controller.StreamAsync(Guid.NewGuid(), null, CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        eventBus.SubscribeAsyncWasCalled.Should().BeFalse("an unauthorized request must never reach the event bus");
    }

    [Fact]
    public async Task StreamAsync_TicketMintedForDifferentTaskId_Returns401_WithoutTouchingEventBus()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var requestedTaskId = Guid.NewGuid(); // a different task than the one this ticket was minted for
        var ticketIssuer = new Mock<ITaskStreamTicketIssuer>();
        Guid outTenantId;
        // The real InMemoryTaskStreamTicketIssuer would itself return false here (that's covered
        // by InMemoryTaskStreamTicketIssuerTests) — this test isolates the controller's own
        // responsibility: trust whatever the issuer says and reject with 401 when it does.
        ticketIssuer.Setup(i => i.TryConsume("valid-ticket", requestedTaskId, out outTenantId))
            .Returns(false);
        var controller = BuildController(ticketIssuer.Object, accessor, out var eventBus);

        await controller.StreamAsync(requestedTaskId, "valid-ticket", CancellationToken.None);

        controller.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
        eventBus.SubscribeAsyncWasCalled.Should().BeFalse(
            "a ticket minted for task X must never be usable against task Y's stream");
    }

    [Fact]
    public async Task StreamAsync_ValidlyScopedTicket_ReachesEventBus_AndSetsAmbientTenantDuringSubscription()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var ticketIssuer = new Mock<ITaskStreamTicketIssuer>();
        Guid outTenantId = tenantId;
        ticketIssuer.Setup(i => i.TryConsume("valid-ticket", taskId, out outTenantId))
            .Returns(true);
        var controller = BuildController(ticketIssuer.Object, accessor, out var eventBus);

        await controller.StreamAsync(taskId, "valid-ticket", CancellationToken.None);

        eventBus.SubscribeAsyncWasCalled.Should().BeTrue("a correctly-scoped ticket must let the request reach the SSE handler");
        eventBus.LastSubscribedTaskId.Should().Be(taskId);
        eventBus.ObservedTenantIdDuringSubscription.Should().Be(tenantId,
            "StreamAsync must set the ambient tenant (mirroring TenantAuthenticationMiddleware) for the subscription's lifetime");
        controller.Response.StatusCode.Should().Be(StatusCodes.Status200OK, "a valid ticket must not be rejected");
        accessor.TenantId.Should().BeNull("the ambient scope must be cleared once the stream ends, exactly like the middleware's own scope");
    }

    [Theory]
    [InlineData("/api/v1/tasks/11111111-1111-1111-1111-111111111111/stream")]
    [InlineData("/api/v1/tasks/11111111-1111-1111-1111-111111111111/Stream")]
    public async Task TenantAuthenticationMiddleware_StreamPath_IsExempt_LettingTheControllerOwnTheAuthDecision(string path)
    {
        // Mirrors RateLimiterSetup.IsExemptPath's existing "/stream" exemption (added for the rate
        // limiter in an earlier task) — TenantAuthenticationMiddleware needs the identical
        // exemption or every real browser EventSource request 401s before the controller's own
        // ticket check ever runs.
        var hasher = new Mock<IApiKeyHasher>();
        var apiKeyRepo = new Mock<IApiKeyRepository>();
        var tenantRepo = new Mock<ITenantRepository>();
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var limitsProvider = new Mock<ITenantLimitsProvider>();
        var middleware = new TenantAuthenticationMiddleware(
            hasher.Object, apiKeyRepo.Object, tenantRepo.Object, accessor, limitsProvider.Object,
            NullLogger<TenantAuthenticationMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        // Deliberately no Authorization header — exactly what a real browser EventSource sends.
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue("the /stream path must bypass Bearer-header auth entirely — the controller validates the ticket instead");
        context.Response.StatusCode.Should().Be(200);
    }
}
