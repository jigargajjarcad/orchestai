using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.API.Middleware;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.API;

public sealed class TenantAuthenticationMiddlewareTests
{
    private static (TenantAuthenticationMiddleware Middleware, Mock<IApiKeyHasher> Hasher,
        Mock<IApiKeyRepository> ApiKeyRepo, Mock<ITenantRepository> TenantRepo, AsyncLocalCurrentTenantAccessor Accessor)
        Build()
    {
        var hasher = new Mock<IApiKeyHasher>();
        var apiKeyRepo = new Mock<IApiKeyRepository>();
        var tenantRepo = new Mock<ITenantRepository>();
        var accessor = new AsyncLocalCurrentTenantAccessor();

        var middleware = new TenantAuthenticationMiddleware(
            hasher.Object, apiKeyRepo.Object, tenantRepo.Object, accessor,
            NullLogger<TenantAuthenticationMiddleware>.Instance);

        return (middleware, hasher, apiKeyRepo, tenantRepo, accessor);
    }

    private static DefaultHttpContext BuildContext(string path, string? authHeader = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (authHeader is not null)
            context.Request.Headers.Authorization = authHeader;
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ValidKey_SetsTenantScopeDuringNextAndCallsIt()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, accessor) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");

        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        apiKeyRepo.Setup(r => r.UpdateAsync(apiKey, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);

        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");
        Guid? observedTenantId = null;
        var nextCalled = false;

        await middleware.InvokeAsync(context, ctx =>
        {
            nextCalled = true;
            observedTenantId = accessor.TenantId;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        observedTenantId.Should().Be(tenant.Id);
        context.Response.StatusCode.Should().Be(200); // untouched default
        accessor.TenantId.Should().BeNull("the ambient scope must be cleared once the request finishes");
    }

    [Fact]
    public async Task InvokeAsync_MissingAuthorizationHeader_Returns401()
    {
        var (middleware, _, _, _, _) = Build();
        var context = BuildContext("/api/v1/eval-suites");
        var nextCalled = false;

        await middleware.InvokeAsync(context, ctx => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_MalformedKey_Returns401()
    {
        var (middleware, hasher, _, _, _) = Build();
        hasher.Setup(h => h.Parse(It.IsAny<string>())).Returns((ParsedApiKey?)null);
        var context = BuildContext("/api/v1/eval-suites", "Bearer garbage");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_UnknownPublicKeyId_Returns401()
    {
        var (middleware, hasher, apiKeyRepo, _, _) = Build();
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync((ApiKey?)null);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_WrongSecret_Returns401()
    {
        var (middleware, hasher, apiKeyRepo, _, _) = Build();
        var apiKey = ApiKey.Create(Guid.NewGuid(), "pk123", "hashed");
        hasher.Setup(h => h.Parse("orch_live_pk123.wrong")).Returns(new ParsedApiKey("pk123", "wrong"));
        hasher.Setup(h => h.Verify("wrong", "hashed")).Returns(false);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.wrong");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_RevokedKey_Returns401()
    {
        var (middleware, hasher, apiKeyRepo, _, _) = Build();
        var apiKey = ApiKey.Create(Guid.NewGuid(), "pk123", "hashed");
        apiKey.Revoke();
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    [Fact]
    public async Task InvokeAsync_ValidKeyForSuspendedTenant_Returns403()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, _) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        tenant.Suspend();
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeFalse();
        context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/swagger/index.html")]
    [InlineData("/api/v1/admin/tenants")]
    public async Task InvokeAsync_ExemptPaths_SkipAuthEntirely(string path)
    {
        var (middleware, _, _, _, _) = Build();
        var context = BuildContext(path);
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_RecentlyUsedKey_DoesNotUpdateLastUsedAtAgain()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, _) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");
        apiKey.RecordUsage(); // simulate a very recent prior use
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");

        await middleware.InvokeAsync(context, _ => Task.CompletedTask);

        apiKeyRepo.Verify(r => r.UpdateAsync(It.IsAny<ApiKey>(), It.IsAny<CancellationToken>()), Times.Never,
            "a key used moments ago must not trigger another LastUsedAt write within the debounce window");
    }

    [Fact]
    public async Task InvokeAsync_UsageRecordingFails_AuthenticationStillSucceeds()
    {
        var (middleware, hasher, apiKeyRepo, tenantRepo, _) = Build();
        var tenant = Tenant.Create("Acme", "acme");
        var apiKey = ApiKey.Create(tenant.Id, "pk123", "hashed");
        hasher.Setup(h => h.Parse("orch_live_pk123.secret")).Returns(new ParsedApiKey("pk123", "secret"));
        hasher.Setup(h => h.Verify("secret", "hashed")).Returns(true);
        apiKeyRepo.Setup(r => r.GetByPublicKeyIdAsync("pk123", It.IsAny<CancellationToken>())).ReturnsAsync(apiKey);
        apiKeyRepo.Setup(r => r.UpdateAsync(apiKey, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("transient DB error"));
        tenantRepo.Setup(r => r.GetByIdAsync(tenant.Id, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        var context = BuildContext("/api/v1/eval-suites", "Bearer orch_live_pk123.secret");
        var nextCalled = false;

        await middleware.InvokeAsync(context, _ => { nextCalled = true; return Task.CompletedTask; });

        nextCalled.Should().BeTrue("a failed best-effort LastUsedAt write must never block authentication");
    }
}
