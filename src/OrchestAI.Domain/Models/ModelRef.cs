namespace OrchestAI.Domain.Models;

// Parses "provider/model" strings from configuration, e.g. "anthropic/claude-haiku-4-5-20251001".
public readonly record struct ModelRef(string ProviderId, string ModelName)
{
    public static ModelRef Parse(string qualifiedModel)
    {
        var separatorIndex = qualifiedModel.IndexOf('/');
        if (separatorIndex <= 0 || separatorIndex == qualifiedModel.Length - 1)
            throw new InvalidOperationException(
                $"Model '{qualifiedModel}' is not in 'provider/model' format.");

        return new ModelRef(
            qualifiedModel[..separatorIndex],
            qualifiedModel[(separatorIndex + 1)..]);
    }
}
