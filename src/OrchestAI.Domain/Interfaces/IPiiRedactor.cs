namespace OrchestAI.Domain.Interfaces;

public interface IPiiRedactor
{
    /// <summary>
    /// Redact PII from text before sending to LLM or storing.
    /// Returns redacted text with placeholders like [EMAIL], [PHONE], [SSN].
    /// </summary>
    string Redact(string input);

    /// <summary>Also reports how many patterns matched, so callers can log with their own context.</summary>
    string Redact(string input, out int matchCount);

    /// <summary>
    /// Whether any redaction rules are configured and active.
    /// If false, skip redaction entirely for performance.
    /// </summary>
    bool IsEnabled { get; }
}
