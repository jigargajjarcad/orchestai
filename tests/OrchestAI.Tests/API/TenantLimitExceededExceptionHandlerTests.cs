using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.API.ExceptionHandling;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.API;

public sealed class TenantLimitExceededExceptionHandlerTests
{
    // TenantLimitExceededExceptionHandler resolves RejectionResponder from
    // HttpContext.RequestServices (not constructor injection) because AddExceptionHandler<T>()
    // registers T as Singleton, and RejectionResponder is Scoped — see the handler's own
    // doc comment. Build a tiny real service provider per test to exercise that resolution path
    // faithfully rather than constructing the handler directly with a responder instance.
    private static HttpContext BuildContext(Stream responseBody, RejectionResponder responder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(responder);
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            Response = { Body = responseBody }
        };
        return context;
    }

    [Fact]
    public async Task TryHandleAsync_OtherExceptionType_ReturnsFalseAndWritesNothing()
    {
        var repoMock = new Mock<IRejectionEventRepository>();
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var responder = new RejectionResponder(repoMock.Object, accessor, NullLogger<RejectionResponder>.Instance);
        var handler = new TenantLimitExceededExceptionHandler();
        var context = BuildContext(new MemoryStream(), responder);

        var handled = await handler.TryHandleAsync(context, new InvalidOperationException("nope"), CancellationToken.None);

        handled.Should().BeFalse();
        repoMock.Verify(r => r.AddAsync(It.IsAny<RejectionEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TryHandleAsync_TenantLimitExceededException_Writes429WithRetryAfterAndReason()
    {
        var repoMock = new Mock<IRejectionEventRepository>();
        RejectionEvent? captured = null;
        Guid? ambientTenantIdDuringPersist = null;
        var accessor = new AsyncLocalCurrentTenantAccessor();
        repoMock.Setup(r => r.AddAsync(It.IsAny<RejectionEvent>(), It.IsAny<CancellationToken>()))
            .Callback<RejectionEvent, CancellationToken>((e, _) =>
            {
                captured = e;
                // RejectionEvent.Create() deliberately never takes TenantId (Global Constraints,
                // Week 11 plan: "never given TenantId explicitly in their factories") — the real
                // TenantScopingInterceptor stamps it from ambient state at actual SaveChangesAsync
                // time, which this mocked repository never invokes. What IS verifiable at this
                // layer, and what this test is actually asserting, is that RejectionResponder has
                // re-opened the correct ambient tenant scope (from the exception, not stale
                // unwound state) for the duration of the persistence call.
                ambientTenantIdDuringPersist = accessor.TenantId;
            })
            .Returns(Task.CompletedTask);
        var responder = new RejectionResponder(repoMock.Object, accessor, NullLogger<RejectionResponder>.Instance);
        var handler = new TenantLimitExceededExceptionHandler();

        var tenantId = Guid.NewGuid();
        var body = new MemoryStream();
        var context = BuildContext(body, responder);
        var exception = new TenantLimitExceededException(
            tenantId, RejectionReason.BudgetExceeded, "Daily budget exceeded.", retryAfterSeconds: 3600,
            detailsJson: """{"limit":50,"actual":52.10}""", traceId: "trace-123");

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        handled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(StatusCodes.Status429TooManyRequests);
        context.Response.Headers.RetryAfter.ToString().Should().Be("3600");

        body.Seek(0, SeekOrigin.Begin);
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("reason").GetString().Should().Be("BudgetExceeded");
        doc.RootElement.GetProperty("retryAfterSeconds").GetInt32().Should().Be(3600);

        captured.Should().NotBeNull();
        ambientTenantIdDuringPersist.Should().Be(tenantId,
            "the exception's captured TenantId must be re-opened as the ambient scope, not stale ambient state, which has already unwound by exception-handling time");
        captured!.Reason.Should().Be(RejectionReason.BudgetExceeded);
        captured.TraceId.Should().Be("trace-123");
    }
}
