using FluentAssertions;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Tests.Domain;

public sealed class ApiKeyTests
{
    [Fact]
    public void Create_IsUsableByDefault()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value", "prod");

        key.IsUsable().Should().BeTrue();
        key.RevokedAt.Should().BeNull();
        key.DisplayName.Should().Be("prod");
    }

    [Fact]
    public void Revoke_MakesKeyUnusable()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value");

        key.Revoke();

        key.IsUsable().Should().BeFalse();
        key.RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public void IsUsable_ExpiredKey_ReturnsFalse()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value");
        typeof(ApiKey).GetProperty(nameof(ApiKey.ExpiresAt))!
            .SetValue(key, DateTimeOffset.UtcNow.AddDays(-1));

        key.IsUsable().Should().BeFalse();
    }

    [Fact]
    public void RecordUsage_SetsLastUsedAt()
    {
        var key = ApiKey.Create(Guid.NewGuid(), "pk_abc123", "hashed-secret-value");

        key.RecordUsage();

        key.LastUsedAt.Should().NotBeNull();
    }
}
