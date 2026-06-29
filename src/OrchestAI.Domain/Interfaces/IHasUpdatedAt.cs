namespace OrchestAI.Domain.Interfaces;

public interface IHasUpdatedAt
{
    DateTimeOffset UpdatedAt { get; }
}
