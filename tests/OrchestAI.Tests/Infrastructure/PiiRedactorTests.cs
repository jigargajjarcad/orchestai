using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Security;

namespace OrchestAI.Tests.Infrastructure;

public sealed class PiiRedactorTests
{
    private static RegexPiiRedactor BuildRedactor(bool enabled = true, IReadOnlyList<PiiCustomRule>? customRules = null)
    {
        var options = Options.Create(new PiiRedactionOptions
        {
            Enabled = enabled,
            CustomRules = customRules ?? []
        });
        return new RegexPiiRedactor(options, NullLogger<RegexPiiRedactor>.Instance);
    }

    [Fact]
    public void Redact_Email_ReplacedWithPlaceholder()
    {
        var redactor = BuildRedactor();
        var result = redactor.Redact("Contact me at jane.doe@example.com for details.");
        result.Should().Be("Contact me at [EMAIL] for details.");
    }

    [Fact]
    public void Redact_PhoneNumber_ReplacedWithPlaceholder()
    {
        var redactor = BuildRedactor();
        var result = redactor.Redact("Call me at 555-123-4567 tomorrow.");
        result.Should().Be("Call me at [PHONE] tomorrow.");
    }

    [Fact]
    public void Redact_Ssn_ReplacedWithPlaceholder()
    {
        var redactor = BuildRedactor();
        var result = redactor.Redact("SSN on file: 123-45-6789.");
        result.Should().Be("SSN on file: [SSN].");
    }

    [Fact]
    public void Redact_VisaCreditCard_ReplacedWithPlaceholder()
    {
        var redactor = BuildRedactor();
        var result = redactor.Redact("Card number 4111111111111111 was charged.");
        result.Should().Be("Card number [CREDIT_CARD] was charged.");
    }

    [Fact]
    public void Redact_MastercardCreditCard_ReplacedWithPlaceholder()
    {
        var redactor = BuildRedactor();
        var result = redactor.Redact("Card number 5500000000000004 was charged.");
        result.Should().Be("Card number [CREDIT_CARD] was charged.");
    }

    [Fact]
    public void Redact_CustomRuleFromConfig_AppliedCorrectly()
    {
        var redactor = BuildRedactor(customRules:
        [
            new PiiCustomRule { Pattern = @"\bMRN-\d{8}\b", Placeholder = "[MEDICAL_ID]" }
        ]);

        var result = redactor.Redact("Patient record MRN-12345678 was updated.");
        result.Should().Be("Patient record [MEDICAL_ID] was updated.");
    }

    [Fact]
    public void Redact_Disabled_InputReturnedUnchanged()
    {
        var redactor = BuildRedactor(enabled: false);
        const string input = "Email jane@example.com and phone 555-123-4567.";

        var result = redactor.Redact(input);

        result.Should().Be(input);
        redactor.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Redact_MultiplePiiTypesInOneString_AllRedacted()
    {
        var redactor = BuildRedactor();
        var input = "Reach Jane at jane@example.com or 555-123-4567. SSN 123-45-6789, card 4111111111111111.";

        var result = redactor.Redact(input);

        result.Should().Contain("[EMAIL]");
        result.Should().Contain("[PHONE]");
        result.Should().Contain("[SSN]");
        result.Should().Contain("[CREDIT_CARD]");
        result.Should().NotContain("jane@example.com");
        result.Should().NotContain("123-45-6789");
    }

    [Fact]
    public void Redact_WithMatchCountOverload_ReportsNumberOfMatches()
    {
        var redactor = BuildRedactor();
        var result = redactor.Redact("Emails: a@example.com and b@example.com.", out var matchCount);

        matchCount.Should().Be(2);
        result.Should().Be("Emails: [EMAIL] and [EMAIL].");
    }

    [Fact]
    public void Redact_InvalidCustomRulePattern_SkippedWithoutThrowing()
    {
        var act = () => BuildRedactor(customRules:
        [
            new PiiCustomRule { Pattern = "[unclosed", Placeholder = "[BAD]" }
        ]);

        act.Should().NotThrow();
    }
}
