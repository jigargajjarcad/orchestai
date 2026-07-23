using FluentAssertions;
using Microsoft.Extensions.Configuration;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Tests.Infrastructure;

public sealed class RequiredConfigurationValidatorTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Covers all six OrchestAI.Domain.Enums.AgentType values. Pinned explicitly (not derived from
    // the enum) so these tests independently confirm the validator checks exactly this set.
    private static Dictionary<string, string?> FullyValidValues() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme",
        ["Anthropic:ApiKey"] = "sk-ant-real-key",
        ["Agents:Models:Orchestrator"] = "claude-sonnet-4-6",
        ["Agents:Models:Research"] = "claude-sonnet-4-6",
        ["Agents:Models:Writer"] = "claude-sonnet-4-6",
        ["Agents:Models:Code"] = "claude-sonnet-4-6",
        ["Agents:Models:Data"] = "claude-sonnet-4-6",
        ["Agents:Models:Browser"] = "claude-sonnet-4-6",
        ["Agents:MaxTokens:Orchestrator"] = "4096",
        ["Agents:MaxTokens:Research"] = "4096",
        ["Agents:MaxTokens:Writer"] = "4096",
        ["Agents:MaxTokens:Code"] = "4096",
        ["Agents:MaxTokens:Data"] = "4096",
        ["Agents:MaxTokens:Browser"] = "4096"
    };

    [Fact]
    public void Validate_AllRequiredKeysPresent_DoesNotThrow()
    {
        var config = BuildConfig(FullyValidValues());

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

    [Fact]
    public void Validate_AgentMissingModelsEntry_ThrowsNamingAgentTypeAndKey()
    {
        var values = FullyValidValues();
        values.Remove("Agents:Models:Research");

        var config = BuildConfig(values);

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Agents:Models:Research*");
    }

    [Fact]
    public void Validate_AgentMissingMaxTokensEntry_ThrowsNamingAgentTypeAndKey()
    {
        var values = FullyValidValues();
        values.Remove("Agents:MaxTokens:Code");

        var config = BuildConfig(values);

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Agents:MaxTokens:Code*");
    }

    [Fact]
    public void Validate_AllSixAgentTypesFullyConfigured_DoesNotThrow()
    {
        var config = BuildConfig(FullyValidValues());

        var act = () => RequiredConfigurationValidator.Validate(config);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_MissingAgentConfiguration_FailsAtValidationTimeNotAsKeyNotFoundDuringExecution()
    {
        // Before this fix (ADR-017 Confirmation #6), a missing Agents:Models/MaxTokens entry
        // surfaced only as a KeyNotFoundException deep inside AgentBase.ExecuteAsync, on first
        // dispatch of that agent type. Validate() takes only IConfiguration — no agent, no
        // execution path is involved here — so a thrown InvalidOperationException from this call
        // alone proves the failure now happens at startup/config-validation time instead.
        var values = FullyValidValues();
        values.Remove("Agents:Models:Browser");
        values.Remove("Agents:MaxTokens:Data");

        var config = BuildConfig(values);

        var act = () => RequiredConfigurationValidator.Validate(config);

        var exception = act.Should().Throw<InvalidOperationException>().Which;
        exception.Should().NotBeOfType<KeyNotFoundException>();
        exception.Message.Should().Contain("Agents:Models:Browser");
        exception.Message.Should().Contain("Agents:MaxTokens:Data");
    }
}
