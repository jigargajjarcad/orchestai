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

        await SeedModelPricingAsync(now, cancellationToken).ConfigureAwait(false);
    }

    // Initial pricing data — ModelPricing is the source of truth going forward (queried by
    // AgentBase.CalculateCostAsync via IModelPricingRepository/ModelPricingCache), not appsettings.
    // ON CONFLICT DO NOTHING so manual price updates made via the DB survive app restarts.
    private async Task SeedModelPricingAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        (string Model, decimal InputPerMillion, decimal OutputPerMillion)[] defaults =
        [
            ("claude-haiku-4-5-20251001", 0.80m, 4.00m),
            ("claude-sonnet-4-6", 3.00m, 15.00m),
            ("gpt-4o", 2.50m, 10.00m),
            ("gpt-4o-mini", 0.15m, 0.60m)
        ];

        var seededCount = 0;
        foreach (var (model, inputPerMillion, outputPerMillion) in defaults)
        {
            var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "ModelPricing" ("Id", "Model", "InputPerMillion", "OutputPerMillion", "UpdatedAt")
                VALUES ({0}, {1}, {2}, {3}, {4})
                ON CONFLICT ("Model") DO NOTHING
                """,
                [Guid.NewGuid(), model, inputPerMillion, outputPerMillion, now],
                cancellationToken).ConfigureAwait(false);

            seededCount += rowsAffected;
        }

        if (seededCount > 0)
            _logger.LogInformation("Seeded {Count} model pricing rows", seededCount);
    }
}
