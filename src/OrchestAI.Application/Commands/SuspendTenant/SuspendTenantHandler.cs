using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.SuspendTenant;

public sealed class SuspendTenantHandler : IRequestHandler<SuspendTenantCommand, SuspendTenantResponse>
{
    private readonly ITenantRepository _tenantRepository;

    public SuspendTenantHandler(ITenantRepository tenantRepository) => _tenantRepository = tenantRepository;

    public async Task<SuspendTenantResponse> Handle(SuspendTenantCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

        tenant.Suspend();
        await _tenantRepository.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);

        return new SuspendTenantResponse(tenant.Id, tenant.Status.ToString());
    }
}
