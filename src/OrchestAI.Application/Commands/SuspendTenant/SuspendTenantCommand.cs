using MediatR;

namespace OrchestAI.Application.Commands.SuspendTenant;

public sealed record SuspendTenantCommand(Guid TenantId) : IRequest<SuspendTenantResponse>;
