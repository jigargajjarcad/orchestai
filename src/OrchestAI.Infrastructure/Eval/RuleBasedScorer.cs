using System.Text.Json;
using System.Text.RegularExpressions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Eval;

// Deterministic scoring for tool-call/format correctness — exact string match, regex, or a
// deliberately minimal JSON Schema check (required-property + primitive-type validation only;
// this is not a full JSON Schema draft implementation, matching the same lightweight schema
// shape already used by ToolInputSchema for MCP tool definitions rather than pulling in a new
// schema-validation dependency for Week 8's scope).
public sealed class RuleBasedScorer : IEvalScorer
{
    public const string Version = "rule-based-v1";

    public EvalScorerType ScorerType => EvalScorerType.RuleBased;

    public Task<EvalScoreResult> ScoreAsync(
        EvalCase evalCase,
        string actualOutput,
        EvalScoringContext context,
        CancellationToken cancellationToken = default)
    {
        using var criteria = JsonDocument.Parse(evalCase.ExpectedCriteria);
        var mode = criteria.RootElement.GetProperty("mode").GetString();

        var (passed, detail) = mode switch
        {
            "ExactMatch" => ScoreExactMatch(criteria.RootElement, actualOutput),
            "Regex" => ScoreRegex(criteria.RootElement, actualOutput),
            "JsonSchema" => ScoreJsonSchema(criteria.RootElement, actualOutput),
            _ => throw new InvalidOperationException($"Unknown RuleBasedScorer mode '{mode}'.")
        };

        var score = passed ? 1.0m : 0.0m;
        var output = JsonSerializer.Serialize(new { mode, passed, detail });

        return Task.FromResult(new EvalScoreResult(score, passed, Version, output));
    }

    private static (bool Passed, string Detail) ScoreExactMatch(JsonElement criteria, string actualOutput)
    {
        var expected = criteria.GetProperty("expected").GetString() ?? string.Empty;
        var passed = string.Equals(actualOutput, expected, StringComparison.Ordinal);
        return (passed, passed ? "exact match" : $"expected '{expected}'");
    }

    private static (bool Passed, string Detail) ScoreRegex(JsonElement criteria, string actualOutput)
    {
        var pattern = criteria.GetProperty("pattern").GetString() ?? string.Empty;
        var passed = Regex.IsMatch(actualOutput, pattern);
        return (passed, passed ? "pattern matched" : $"did not match /{pattern}/");
    }

    private static (bool Passed, string Detail) ScoreJsonSchema(JsonElement criteria, string actualOutput)
    {
        JsonDocument actualDoc;
        try
        {
            actualDoc = JsonDocument.Parse(actualOutput);
        }
        catch (JsonException)
        {
            return (false, "actual output is not valid JSON");
        }

        using (actualDoc)
        {
            var schema = criteria.GetProperty("schema");
            var properties = schema.TryGetProperty("properties", out var p) ? p : default;
            var required = schema.TryGetProperty("required", out var r)
                ? r.EnumerateArray().Select(e => e.GetString()!).ToList()
                : [];

            foreach (var requiredProp in required)
            {
                if (!actualDoc.RootElement.TryGetProperty(requiredProp, out var actualValue))
                    return (false, $"missing required property '{requiredProp}'");

                if (properties.ValueKind == JsonValueKind.Object
                    && properties.TryGetProperty(requiredProp, out var propSchema)
                    && propSchema.TryGetProperty("type", out var typeEl)
                    && !MatchesJsonType(actualValue, typeEl.GetString()!))
                {
                    return (false, $"property '{requiredProp}' does not match declared type");
                }
            }

            return (true, "schema satisfied");
        }
    }

    private static bool MatchesJsonType(JsonElement value, string jsonType) => jsonType switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        _ => true
    };
}
