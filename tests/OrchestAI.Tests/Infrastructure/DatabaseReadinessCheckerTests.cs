using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

// Uses a dedicated, disposable database (orchestai_readiness_test) rather than the shared
// `orchestai` database every other integration test in this project relies on — this test
// deliberately migrates a database backward (to a known prior migration) and forward again,
// which would corrupt the shared dev database for every concurrently-running test if done
// in-place. The connecting role (orchestai) is the docker-compose Postgres image's bootstrap
// superuser, so CREATE DATABASE/DROP DATABASE both succeed without extra grants.
public sealed class DatabaseReadinessCheckerTests : IAsyncLifetime
{
    private const string AdminConnectionString =
        "Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme";
    private const string TestConnectionString =
        "Host=localhost;Port=5432;Database=orchestai_readiness_test;Username=orchestai;Password=changeme";

    // The migration immediately before the current latest — see
    // src/OrchestAI.Infrastructure/Migrations/. If a later week adds new migrations, this still
    // proves the same behavior (some known-older point reports NotReady; latest reports Ready);
    // it doesn't need updating unless this specific migration is ever removed.
    private const string PriorMigration = "20260715084549_AddIdempotencyRecords";

    public async Task InitializeAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using (var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS orchestai_readiness_test WITH (FORCE)", admin))
            await drop.ExecuteNonQueryAsync();
        await using (var create = new NpgsqlCommand("CREATE DATABASE orchestai_readiness_test", admin))
            await create.ExecuteNonQueryAsync();

        // WITH (FORCE) terminates sessions on the server, but Npgsql's client-side ADO.NET
        // connection pool doesn't know that — a previous test's now-dead pooled physical
        // connection (keyed by this exact connection string) would otherwise get handed back
        // out here and fail on first use with "57P01: terminating connection due to
        // administrator command". Clear the pool so every test opens a fresh connection
        // against the newly (re)created database.
        NpgsqlConnection.ClearAllPools();
    }

    public async Task DisposeAsync()
    {
        await using var admin = new NpgsqlConnection(AdminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS orchestai_readiness_test WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private static AppDbContext BuildContext(string connectionString)
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        return new AppDbContext(options, accessor);
    }

    [Fact]
    public async Task CheckAsync_PendingMigrations_ReportsNotReady()
    {
        await using var dbContext = BuildContext(TestConnectionString);
        var migrator = dbContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync(PriorMigration);

        var checker = new DatabaseReadinessChecker(dbContext, NullLogger<DatabaseReadinessChecker>.Instance);
        var result = await checker.CheckAsync(CancellationToken.None);

        result.IsReady.Should().BeFalse();
        result.Reason.Should().Contain("pending migrations");
    }

    [Fact]
    public async Task CheckAsync_AllMigrationsApplied_ReportsReady()
    {
        await using var dbContext = BuildContext(TestConnectionString);
        var migrator = dbContext.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync();

        var checker = new DatabaseReadinessChecker(dbContext, NullLogger<DatabaseReadinessChecker>.Instance);
        var result = await checker.CheckAsync(CancellationToken.None);

        result.IsReady.Should().BeTrue();
        result.Reason.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_DatabaseUnreachable_ReportsNotReady()
    {
        var unreachable =
            "Host=localhost;Port=59999;Database=orchestai;Username=orchestai;Password=changeme;Timeout=2";
        await using var dbContext = BuildContext(unreachable);

        var checker = new DatabaseReadinessChecker(dbContext, NullLogger<DatabaseReadinessChecker>.Instance);
        var result = await checker.CheckAsync(CancellationToken.None);

        result.IsReady.Should().BeFalse();
        result.Reason.Should().Be("database unreachable");
    }
}
