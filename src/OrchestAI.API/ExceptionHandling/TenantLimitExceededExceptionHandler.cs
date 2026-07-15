using Microsoft.AspNetCore.Diagnostics;
using OrchestAI.Application.Exceptions;

namespace OrchestAI.API.ExceptionHandling;

// Single choke point for every synchronous-HTTP-request rejection thrown as an exception
// (ConcurrencyExceeded, BudgetExceeded, QueueBackpressure). RateLimited is handled separately
// by the rate limiter's own OnRejected callback (not an exception — see Task 9).
// AgentCapExceeded is handled inline inside the background dispatch (Task 8), since it has no
// HTTP response in flight to write to. Composes with the existing
// UseExceptionHandler()/AddProblemDetails() already configured in Program.cs.
//
// RejectionResponder is resolved from HttpContext.RequestServices (the per-request scope)
// rather than constructor-injected: AddExceptionHandler<T>() registers T as Singleton (ASP.NET
// Core resolves IExceptionHandler once for the app's lifetime), but RejectionResponder is Scoped
// (it depends on the Scoped IRejectionEventRepository). Constructor-injecting a Scoped service
// into a Singleton is a captive-dependency error that ASP.NET Core's startup service-provider
// validation rejects outright in Development — confirmed by actually running the host (`dotnet
// ef migrations add`, which builds the real service provider) rather than trusting the brief's
// snippet at face value. See Task 2's self-review notes.
public sealed class TenantLimitExceededExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not TenantLimitExceededException tle)
            return false;

        var responder = httpContext.RequestServices.GetRequiredService<RejectionResponder>();
        await responder.RespondToExceptionAsync(httpContext, tle, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
