using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class RequireAdminSecretFilterTests
{
    private static ActionExecutingContext BuildContext(string? headerValue)
    {
        var httpContext = new DefaultHttpContext();
        if (headerValue is not null)
            httpContext.Request.Headers["X-Admin-Secret"] = headerValue;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
        return new ActionExecutingContext(
            actionContext, [], new Dictionary<string, object?>(), controller: new object());
    }

    private static IConfiguration BuildConfig(string? secret) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(secret is null ? [] : new Dictionary<string, string?> { ["Admin:BootstrapSecret"] = secret })
            .Build();

    [Fact]
    public async Task OnActionExecutionAsync_CorrectSecret_CallsNext()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var context = BuildContext("correct-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WrongSecret_Returns401AndDoesNotCallNext()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var context = BuildContext("wrong-secret");
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        nextCalled.Should().BeFalse();
        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_MissingHeader_Returns401()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var context = BuildContext(headerValue: null);

        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], context.Controller)));

        context.Result.Should().BeOfType<UnauthorizedResult>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_SecretNotConfigured_Returns503()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig(secret: null));
        var context = BuildContext("anything");

        await filter.OnActionExecutionAsync(context, () => Task.FromResult(new ActionExecutedContext(context, [], context.Controller)));

        context.Result.Should().BeOfType<StatusCodeResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
    }

    // A legitimate tenant credential must never satisfy the admin gate — this filter checks
    // ONLY X-Admin-Secret, never Authorization, so a request carrying a real (or fake) tenant
    // API key but no X-Admin-Secret header is indistinguishable from any other unauthenticated
    // request as far as this filter is concerned. Named explicitly (rather than left as an
    // implication of "checks one specific header") because it's the exact invariant confirmation
    // #8 depends on: an ordinary tenant must never be able to reach tenant/API-key provisioning.
    [Fact]
    public async Task OnActionExecutionAsync_ValidTenantAuthorizationHeaderPresent_StillReturns401()
    {
        var filter = new RequireAdminSecretFilter(BuildConfig("correct-secret"));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Authorization = "Bearer orch_live_pk123.a-real-tenant-secret";
        var actionContext = new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
        var context = new ActionExecutingContext(
            actionContext, [], new Dictionary<string, object?>(), controller: new object());
        var nextCalled = false;

        await filter.OnActionExecutionAsync(context, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });

        nextCalled.Should().BeFalse("a tenant API key must never satisfy the admin-secret gate, regardless of how valid it is elsewhere");
        context.Result.Should().BeOfType<UnauthorizedResult>();
    }
}
