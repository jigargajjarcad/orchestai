namespace OrchestAI.Application.Commands.CreateTenant;

public sealed record CreateTenantResponse(Guid TenantId, string Name, string Slug, DateTimeOffset CreatedAt);
