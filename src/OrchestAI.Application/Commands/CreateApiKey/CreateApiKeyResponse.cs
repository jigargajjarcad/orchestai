namespace OrchestAI.Application.Commands.CreateApiKey;

// RawKey is returned exactly once — the caller must store it now; it can never be retrieved
// again (only HashedSecret is persisted). See ADR-014 confirmation #7.
public sealed record CreateApiKeyResponse(Guid ApiKeyId, string RawKey, string PublicKeyId, DateTimeOffset CreatedAt);
