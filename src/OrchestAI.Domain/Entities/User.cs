using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Domain.Entities;

public sealed class User : IHasUpdatedAt
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<OrchestrationTask> _tasks = [];
    public IReadOnlyCollection<OrchestrationTask> Tasks => _tasks.AsReadOnly();

    public static User Create(string email, string displayName)
    {
        return new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }
}
