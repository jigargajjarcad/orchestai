namespace OrchestAI.Domain.Interfaces;

// Short-lived, single-use, (tenantId, taskId)-bound opaque ticket that lets a browser's native
// EventSource (which cannot send an Authorization header — a hard platform limitation, not a bug)
// open GET {id}/stream without a Bearer key. Minted by an authenticated, normal-Bearer-auth
// POST {id}/stream-ticket call; consumed exactly once by the stream endpoint itself. See Task 1
// (Phase 1 architecture/product validation) and TenantAuthenticationMiddleware's /stream
// exemption.
public interface ITaskStreamTicketIssuer
{
    // Mints a single-use ticket bound to exactly this (tenantId, taskId) pair.
    string Issue(Guid tenantId, Guid taskId);

    // Consumes (and invalidates) the ticket if — and only if — it exists, has not
    // expired, and was minted for this exact taskId. Returns the bound tenantId on
    // success so the caller can set ambient tenant context, same as the normal
    // Bearer-auth path does.
    bool TryConsume(string? ticket, Guid taskId, out Guid tenantId);
}
