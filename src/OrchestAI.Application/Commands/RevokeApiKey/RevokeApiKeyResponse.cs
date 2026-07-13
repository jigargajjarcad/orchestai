namespace OrchestAI.Application.Commands.RevokeApiKey;

public sealed record RevokeApiKeyResponse(Guid ApiKeyId, bool Revoked);
