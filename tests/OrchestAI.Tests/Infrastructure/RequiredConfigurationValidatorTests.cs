using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Infrastructure;

public sealed class RequiredConfigurationValidatorTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Validate_AllRequiredKeysPresent_DoesNotThrow()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme",
            ["Anthropic:ApiKey"] = "sk-ant-real-key"
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MissingConnectionString_ThrowsWithClearMessage()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Anthropic:ApiKey"] = "sk-ant-real-key"
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:DefaultConnection*");
    }

    [Fact]
    public void Validate_MissingAnthropicApiKey_ThrowsWithClearMessage()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme"
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Anthropic:ApiKey*");
    }

    [Fact]
    public void Validate_BlankValues_AreTreatedAsMissing()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "   ",
            ["Anthropic:ApiKey"] = ""
        });

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ConnectionStrings:DefaultConnection*")
            .WithMessage("*Anthropic:ApiKey*");
    }

    [Fact]
    public void Validate_MultipleMissingKeys_ListsAllOfThemInOneException()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        var act = () => RequiredConfigurationValidator.Validate(config);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Message.Should().Contain("ConnectionStrings:DefaultConnection");
        exception.Message.Should().Contain("Anthropic:ApiKey");
    }
}
