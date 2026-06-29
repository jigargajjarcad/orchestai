namespace OrchestAI.Domain.Models;

public sealed record ToolInputSchema(
    string Type,
    IReadOnlyDictionary<string, ToolProperty> Properties,
    IReadOnlyList<string> Required
);

public sealed record ToolProperty(string Type, string Description, string[]? Enum = null);
