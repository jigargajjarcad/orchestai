using MediatR;

namespace OrchestAI.Application.Commands.CreateTenant;

public sealed record CreateTenantCommand(string Name, string Slug) : IRequest<CreateTenantResponse>;
