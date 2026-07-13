using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.RevokeApiKey;

public sealed class RevokeApiKeyHandler : IRequestHandler<RevokeApiKeyCommand, RevokeApiKeyResponse>
{
    private readonly IApiKeyRepository _apiKeyRepository;

    public RevokeApiKeyHandler(IApiKeyRepository apiKeyRepository) => _apiKeyRepository = apiKeyRepository;

    public async Task<RevokeApiKeyResponse> Handle(RevokeApiKeyCommand request, CancellationToken cancellationToken)
    {
        var apiKey = await _apiKeyRepository.GetByIdAsync(request.ApiKeyId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(ApiKey), request.ApiKeyId);

        apiKey.Revoke();
        await _apiKeyRepository.UpdateAsync(apiKey, cancellationToken).ConfigureAwait(false);

        return new RevokeApiKeyResponse(apiKey.Id, Revoked: true);
    }
}
