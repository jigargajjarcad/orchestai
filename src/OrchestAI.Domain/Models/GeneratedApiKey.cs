namespace OrchestAI.Domain.Models;

// RawKey is shown to the caller exactly once (at creation) and never persisted or logged
// anywhere — only HashedSecret is stored. See ADR-014 confirmation #7.
public sealed record GeneratedApiKey(string RawKey, string PublicKeyId, string HashedSecret);
