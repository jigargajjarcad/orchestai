using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace OrchestAI.Infrastructure.Tenancy;

// Gates the admin-bootstrap controller (Tenant/ApiKey creation) — deliberately separate from
// the tenant API-key auth middleware (Task 9). An ordinary tenant must never be able to create
// another tenant or mint itself unlimited keys; this is operator-only, gated by a single static
// secret configured out-of-band (env var), never a tenant API key. See ADR-014 confirmation #8.
public sealed class RequireAdminSecretFilter : IAsyncActionFilter
{
    private readonly IConfiguration _configuration;

    public RequireAdminSecretFilter(IConfiguration configuration) => _configuration = configuration;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expectedSecret = _configuration["Admin:BootstrapSecret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Admin-Secret", out var provided) ||
            !ConstantTimeEquals(provided.ToString(), expectedSecret))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        await next().ConfigureAwait(false);
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
