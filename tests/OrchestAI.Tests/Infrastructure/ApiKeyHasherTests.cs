using FluentAssertions;
using OrchestAI.Infrastructure.Security;

namespace OrchestAI.Tests.Infrastructure;

public sealed class ApiKeyHasherTests
{
    [Fact]
    public void GenerateNew_ProducesCorrectlyFormattedKey()
    {
        var hasher = new ApiKeyHasher();

        var generated = hasher.GenerateNew();

        generated.RawKey.Should().StartWith("orch_live_");
        generated.RawKey.Should().Contain(".");
        generated.PublicKeyId.Should().HaveLength(12);
        generated.HashedSecret.Should().NotBeNullOrEmpty();
        generated.RawKey.Should().NotContain(generated.HashedSecret, "the raw key must never embed the hash — only the plaintext secret");
    }

    [Fact]
    public void GenerateNew_TwoCalls_ProduceDifferentKeys()
    {
        var hasher = new ApiKeyHasher();

        var first = hasher.GenerateNew();
        var second = hasher.GenerateNew();

        first.RawKey.Should().NotBe(second.RawKey);
        first.PublicKeyId.Should().NotBe(second.PublicKeyId);
    }

    [Fact]
    public void Parse_RoundTripsAGeneratedKey()
    {
        var hasher = new ApiKeyHasher();
        var generated = hasher.GenerateNew();

        var parsed = hasher.Parse(generated.RawKey);

        parsed.Should().NotBeNull();
        parsed!.PublicKeyId.Should().Be(generated.PublicKeyId);
        hasher.Verify(parsed.RawSecret, generated.HashedSecret).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key-at-all")]
    [InlineData("orch_live_missingdot")]
    [InlineData("orch_live_.emptypublickeyid")]
    [InlineData("orch_live_abc123.")]
    [InlineData("wrong_prefix_abc123.secret")]
    public void Parse_MalformedInput_ReturnsNull(string input)
    {
        var hasher = new ApiKeyHasher();

        var parsed = hasher.Parse(input);

        parsed.Should().BeNull();
    }

    [Fact]
    public void Verify_CorrectSecret_ReturnsTrue()
    {
        var hasher = new ApiKeyHasher();
        var hashed = hasher.Hash("correct-secret-value");

        hasher.Verify("correct-secret-value", hashed).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongSecret_ReturnsFalse()
    {
        var hasher = new ApiKeyHasher();
        var hashed = hasher.Hash("correct-secret-value");

        hasher.Verify("wrong-secret-value", hashed).Should().BeFalse();
    }

    [Fact]
    public void Verify_MalformedStoredHash_ReturnsFalseInsteadOfThrowing()
    {
        var hasher = new ApiKeyHasher();

        var act = () => hasher.Verify("some-secret", "not-valid-hex!!");

        act.Should().NotThrow();
        hasher.Verify("some-secret", "not-valid-hex!!").Should().BeFalse();
    }

    [Fact]
    public void Hash_IsDeterministic()
    {
        var hasher = new ApiKeyHasher();

        hasher.Hash("same-input").Should().Be(hasher.Hash("same-input"));
    }
}
