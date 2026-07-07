using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Security;

public sealed class RegexPiiRedactor : IPiiRedactor
{
    private static readonly IReadOnlyList<(Regex Pattern, string Placeholder)> BuiltInRules =
    [
        (new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled), "[EMAIL]"),
        (new Regex(@"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b", RegexOptions.Compiled), "[PHONE]"),
        (new Regex(@"\b\d{3}-\d{2}-\d{4}\b", RegexOptions.Compiled), "[SSN]"),
        (new Regex(@"\b4[0-9]{12}(?:[0-9]{3})?\b", RegexOptions.Compiled), "[CREDIT_CARD]"),  // Visa
        (new Regex(@"\b5[1-5][0-9]{14}\b", RegexOptions.Compiled), "[CREDIT_CARD]"),           // Mastercard
    ];

    private readonly IReadOnlyList<(Regex Pattern, string Placeholder)> _customRules;
    private readonly ILogger<RegexPiiRedactor> _logger;

    public bool IsEnabled { get; }

    public RegexPiiRedactor(IOptions<PiiRedactionOptions> options, ILogger<RegexPiiRedactor> logger)
    {
        _logger = logger;
        IsEnabled = options.Value.Enabled;

        var customRules = new List<(Regex, string)>();
        foreach (var rule in options.Value.CustomRules)
        {
            try
            {
                customRules.Add((new Regex(rule.Pattern, RegexOptions.Compiled), rule.Placeholder));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalid PII custom rule pattern '{Pattern}' — skipping", rule.Pattern);
            }
        }
        _customRules = customRules;
    }

    public string Redact(string input) => Redact(input, out _);

    /// <summary>Also reports how many patterns matched, so callers can log with their own context.</summary>
    public string Redact(string input, out int matchCount)
    {
        matchCount = 0;
        if (!IsEnabled || string.IsNullOrEmpty(input))
            return input;

        var redacted = input;
        var count = 0;

        foreach (var (pattern, placeholder) in BuiltInRules)
            redacted = pattern.Replace(redacted, m => { count++; return placeholder; });

        foreach (var (pattern, placeholder) in _customRules)
            redacted = pattern.Replace(redacted, m => { count++; return placeholder; });

        matchCount = count;
        return redacted;
    }
}
