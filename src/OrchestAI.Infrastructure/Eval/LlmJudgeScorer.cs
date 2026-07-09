using System.Text.Json;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Eval;

// Non-deterministic scoring via an LLM judge. Temperature is pinned to 0 for every call —
// per ADR-012 this makes scores *directionally* reliable across runs, not bit-exact
// reproducible; that's a documented, accepted limitation, not a bug. The judge call itself is
// cost-tracked exactly like an agent turn (same ModelPricing lookup, same CostLedger row shape)
// but tagged Source=Eval/EvalRunId so it never reaches production cost dashboards — see
// CostLedgerRepository.GetDailyAggregatesAsync's Source filter (Task 8).
public sealed class LlmJudgeScorer : IEvalScorer
{
    public const string Version = "llm-judge-v1";

    private const string JudgeSystemPrompt =
        """
        You are an evaluation judge. Given a rubric and an agent's actual output, score how well
        the output satisfies the rubric from 0.0 (fails completely) to 1.0 (fully satisfies it).
        Return ONLY valid JSON, no markdown:
        {"score": 0.0, "reasoning": "one sentence explaining the score"}
        """;

    private readonly ILlmProviderFactory _providerFactory;
    private readonly IModelPricingCache _pricingCache;
    private readonly ICostLedgerRepository _costLedgerRepository;
    private readonly IOptions<EvalOptions> _options;

    public LlmJudgeScorer(
        ILlmProviderFactory providerFactory,
        IModelPricingCache pricingCache,
        ICostLedgerRepository costLedgerRepository,
        IOptions<EvalOptions> options)
    {
        _providerFactory = providerFactory;
        _pricingCache = pricingCache;
        _costLedgerRepository = costLedgerRepository;
        _options = options;
    }

    public EvalScorerType ScorerType => EvalScorerType.LlmJudge;

    public async Task<EvalScoreResult> ScoreAsync(
        EvalCase evalCase,
        string actualOutput,
        EvalScoringContext context,
        CancellationToken cancellationToken = default)
    {
        using var criteria = JsonDocument.Parse(evalCase.ExpectedCriteria);
        var rubric = criteria.RootElement.GetProperty("rubric").GetString() ?? string.Empty;
        var passThreshold = criteria.RootElement.TryGetProperty("passThreshold", out var thresholdEl)
            ? thresholdEl.GetDecimal()
            : _options.Value.DefaultJudgePassThreshold;

        var modelRef = ModelRef.Parse(_options.Value.JudgeModel);
        var provider = _providerFactory.Resolve(modelRef.ProviderId);

        var conversation = new AgentConversation(
            JudgeSystemPrompt,
            Messages: [new ConversationMessage("user", $"Rubric: {rubric}\n\nActual output:\n{actualOutput}")],
            Tools: [],
            Model: modelRef.ModelName,
            MaxTokens: 256,
            Temperature: 0.0);

        var turn = await provider.SendAsync(conversation, cancellationToken).ConfigureAwait(false);

        // Cost is incurred the moment the provider call returns, regardless of whether the
        // judge's response can be parsed afterward — so the ledger write happens BEFORE
        // ParseJudgeResponse. Otherwise a malformed judge response would throw and the API
        // call would be billed by the provider but never recorded in our ledger.
        var costUsd = await CalculateCostAsync(modelRef.ModelName, turn.InputTokens, turn.OutputTokens, cancellationToken)
            .ConfigureAwait(false);

        var ledger = CostLedger.Create(
            context.OrchestrationTaskId,
            _options.Value.JudgeModel,
            turn.InputTokens, turn.OutputTokens, costUsd,
            agentExecutionId: null,
            source: CostSource.Eval,
            evalRunId: context.EvalRunId);
        await _costLedgerRepository.AddAsync(ledger, cancellationToken).ConfigureAwait(false);

        var (score, reasoning) = ParseJudgeResponse(turn.Text);
        var passed = score >= passThreshold;

        var outputJson = JsonSerializer.Serialize(new { score, reasoning, passThreshold });
        return new EvalScoreResult(score, passed, Version, outputJson);
    }

    // Best-effort — the judge's own response is external LLM output and may be malformed or
    // missing fields. Never throw here: a thrown exception would propagate out of ScoreAsync
    // and silently drop this case's result from the eval run. Fall back to a definite failing
    // score instead, mirroring AgentBase.ParseMemoryExtractions's try/catch(JsonException) pattern.
    private static (decimal Score, string Reasoning) ParseJudgeResponse(string judgeText)
    {
        var cleaned = judgeText.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var start = cleaned.IndexOf('\n') + 1;
            var end = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) cleaned = cleaned[start..end].Trim();
        }

        try
        {
            using var doc = JsonDocument.Parse(cleaned);
            if (!doc.RootElement.TryGetProperty("score", out var scoreEl) || !scoreEl.TryGetDecimal(out var score))
                return (0m, "failed to parse judge response");

            var reasoning = doc.RootElement.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
            return (score, reasoning);
        }
        catch (JsonException)
        {
            return (0m, "failed to parse judge response");
        }
    }

    private async Task<decimal> CalculateCostAsync(
        string model, int inputTokens, int outputTokens, CancellationToken cancellationToken)
    {
        var pricing = await _pricingCache.GetAsync(model, cancellationToken).ConfigureAwait(false);
        if (pricing is null) return 0m;

        return (inputTokens / 1_000_000m) * pricing.InputPerMillion
             + (outputTokens / 1_000_000m) * pricing.OutputPerMillion;
    }
}
