using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateTenant;

public sealed class CreateTenantHandler : IRequestHandler<CreateTenantCommand, CreateTenantResponse>
{
    private readonly ITenantRepository _tenantRepository;

    public CreateTenantHandler(ITenantRepository tenantRepository) => _tenantRepository = tenantRepository;

    public async Task<CreateTenantResponse> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException(nameof(request.Name), "Name is required.");
        if (string.IsNullOrWhiteSpace(request.Slug))
            throw new ValidationException(nameof(request.Slug), "Slug is required.");

        var existing = await _tenantRepository.GetBySlugAsync(request.Slug, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
            throw new ValidationException(nameof(request.Slug), $"Slug '{request.Slug}' is already in use.");

        var tenant = Tenant.Create(request.Name, request.Slug);
        await _tenantRepository.AddAsync(tenant, cancellationToken).ConfigureAwait(false);

        return new CreateTenantResponse(tenant.Id, tenant.Name, tenant.Slug, tenant.CreatedAt);
    }
}
