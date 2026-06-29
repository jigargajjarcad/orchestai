using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace OrchestAI.Infrastructure.Data;

public sealed class DatabaseSeeder
{
    public static readonly Guid DevUserId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6");

    private readonly AppDbContext _context;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(AppDbContext context, ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await _context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;

        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "Email", "DisplayName", "CreatedAt", "UpdatedAt")
            VALUES ({0}, {1}, {2}, {3}, {4})
            ON CONFLICT ("Id") DO NOTHING
            """,
            [DevUserId, "dev@orchestai.local", "Dev User", now, now],
            cancellationToken).ConfigureAwait(false);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "Seeded dev user {UserId} (dev@orchestai.local)", DevUserId);
        }
    }
}
