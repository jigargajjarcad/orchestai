namespace OrchestAI.Domain.Entities;

// The raw secret is never persisted or logged — only HashedSecret (see ADR-014 confirmation
// #7). PublicKeyId is the indexed lookup key; HashedSecret is verified via constant-time
// comparison against the caller-supplied secret (see IApiKeyHasher, Task 7).
public sealed class ApiKey
{
    private ApiKey() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string PublicKeyId { get; private set; } = string.Empty;
    public string HashedSecret { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastUsedAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public Tenant Tenant { get; private set; } = null!;

    public static ApiKey Create(Guid tenantId, string publicKeyId, string hashedSecret, string? displayName = null)
    {
        return new ApiKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PublicKeyId = publicKeyId,
            HashedSecret = hashedSecret,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public bool IsUsable() =>
        RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);

    public void Revoke() => RevokedAt = DateTimeOffset.UtcNow;

    public void RecordUsage() => LastUsedAt = DateTimeOffset.UtcNow;
}
