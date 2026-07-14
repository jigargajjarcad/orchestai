using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateApiKey;

public sealed class CreateApiKeyHandler : IRequestHandler<CreateApiKeyCommand, CreateApiKeyResponse>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IApiKeyRepository _apiKeyRepository;
    private readonly IApiKeyHasher _hasher;

    public CreateApiKeyHandler(
        ITenantRepository tenantRepository, IApiKeyRepository apiKeyRepository, IApiKeyHasher hasher)
    {
        _tenantRepository = tenantRepository;
        _apiKeyRepository = apiKeyRepository;
        _hasher = hasher;
    }

    public async Task<CreateApiKeyResponse> Handle(CreateApiKeyCommand request, CancellationToken cancellationToken)
    {
        // Global Constraint: the default/backfill tenant must be structurally incapable of
        // authenticating — no ApiKey row is ever created for it, under any circumstances,
        // including operator error. See Tenant.DefaultTenantId (Task 1), ADR-014 confirmation #8.
        if (request.TenantId == Tenant.DefaultTenantId)
            throw new ValidationException(nameof(request.TenantId), "Cannot create an API key for the default/system tenant.");

        _ = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(Tenant), request.TenantId);

        var generated = _hasher.GenerateNew();
        var apiKey = ApiKey.Create(request.TenantId, generated.PublicKeyId, generated.HashedSecret, request.DisplayName);
        await _apiKeyRepository.AddAsync(apiKey, cancellationToken).ConfigureAwait(false);

        return new CreateApiKeyResponse(apiKey.Id, generated.RawKey, generated.PublicKeyId, apiKey.CreatedAt);
    }
}
