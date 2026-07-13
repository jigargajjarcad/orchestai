using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IApiKeyHasher
{
    GeneratedApiKey GenerateNew();

    // Returns null for anything that doesn't match "orch_live_<publicKeyId>.<secret>" with a
    // non-empty publicKeyId and secret — malformed input, not an exception.
    ParsedApiKey? Parse(string rawKey);

    string Hash(string rawSecret);

    // Constant-time comparison — never a raw string == / Equals. A malformed stored hash
    // returns false, it never throws (an auth check must fail closed on bad data, not crash).
    bool Verify(string rawSecret, string hashedSecret);
}
