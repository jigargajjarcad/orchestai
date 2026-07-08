namespace OrchestAI.Domain.Entities;

// DB-backed replacement for appsettings.json:Pricing — admin-updatable without a redeploy.
// Keyed by the bare model name (e.g. "claude-haiku-4-5-20251001"), matching how
// AgentBase.CalculateCost already looks pricing up — CostLedger.Model stores the
// provider-qualified string ("anthropic/claude-...") for display; pricing lookup strips
// the provider prefix first, same as before this table existed.
public sealed class ModelPricing
{
    private ModelPricing() { }

    public Guid Id { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public decimal InputPerMillion { get; private set; }
    public decimal OutputPerMillion { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static ModelPricing Create(string model, decimal inputPerMillion, decimal outputPerMillion)
    {
        return new ModelPricing
        {
            Id = Guid.NewGuid(),
            Model = model,
            InputPerMillion = inputPerMillion,
            OutputPerMillion = outputPerMillion,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdatePricing(decimal inputPerMillion, decimal outputPerMillion)
    {
        InputPerMillion = inputPerMillion;
        OutputPerMillion = outputPerMillion;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
