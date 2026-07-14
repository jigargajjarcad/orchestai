namespace OrchestAI.Application.Commands.SuspendTenant;

public sealed record SuspendTenantResponse(Guid TenantId, string Status);
