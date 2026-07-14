using MediatR;

namespace OrchestAI.Application.Commands.RevokeApiKey;

public sealed record RevokeApiKeyCommand(Guid ApiKeyId) : IRequest<RevokeApiKeyResponse>;
