using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Data;

// Backs /health/ready (see Program.cs). Checks DB connectivity first, then pending migrations —
// in that order, since GetPendingMigrationsAsync itself needs a working connection and would
// throw a less clear error if the DB is simply unreachable.
public sealed class DatabaseReadinessChecker : IReadinessChecker
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<DatabaseReadinessChecker> _logger;

    public DatabaseReadinessChecker(AppDbContext dbContext, ILogger<DatabaseReadinessChecker> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        bool canConnect;
        try
        {
            canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness check: database connection attempt threw");
            return new ReadinessResult(false, "database unreachable");
        }

        if (!canConnect)
            return new ReadinessResult(false, "database unreachable");

        List<string> pending;
        try
        {
            pending = (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Readiness check: failed to read pending migrations");
            return new ReadinessResult(false, "unable to determine migration state");
        }

        return pending.Count == 0
            ? new ReadinessResult(true, null)
            : new ReadinessResult(false, $"pending migrations: {string.Join(", ", pending)}");
    }
}
