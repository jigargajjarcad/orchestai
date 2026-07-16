using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Data.Interceptors;
using OrchestAI.Infrastructure.Repositories;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class IdempotencyRecordExpiryTests
{
    private sealed class SingleContextFactory(DbContextOptions<AppDbContext> options, ICurrentTenantAccessor accessor)
        : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options, accessor);
        public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private static (IDbContextFactory<AppDbContext> Factory, AsyncLocalCurrentTenantAccessor Accessor) CreateInMemoryFactory()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new TenantScopingInterceptor(accessor))
            .Options;
        return (new SingleContextFactory(options, accessor), accessor);
    }

    [Fact]
    public async Task GetByKeyAsync_RecordExpired_ReturnsNull()
    {
        var (factory, accessor) = CreateInMemoryFactory();
        var tenant = Tenant.Create("Acme", $"acme-{Guid.NewGuid():N}");
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
        }

        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            // Create's ttl is relative to "now" — a negative TimeSpan produces an already-past ExpiresAt.
            var expired = IdempotencyRecord.Create("key-1", Guid.NewGuid(), "hash", TimeSpan.FromSeconds(-1));
            ctx.IdempotencyRecords.Add(expired);
            await ctx.SaveChangesAsync();
        }

        IdempotencyRecord? found;
        using (accessor.SetTenant(tenant.Id))
        {
            var repository = new IdempotencyRecordRepository(factory);
            found = await repository.GetByKeyAsync("key-1");
        }

        found.Should().BeNull("an expired idempotency key must be treated as absent, freeing it for reuse on a brand-new task");
    }

    [Fact]
    public async Task GetByKeyAsync_RecordNotYetExpired_ReturnsIt()
    {
        var (factory, accessor) = CreateInMemoryFactory();
        var tenant = Tenant.Create("Acme", $"acme-{Guid.NewGuid():N}");
        await using (var ctx = await factory.CreateDbContextAsync())
        {
            ctx.Tenants.Add(tenant);
            await ctx.SaveChangesAsync();
        }

        var taskId = Guid.NewGuid();
        using (accessor.SetTenant(tenant.Id))
        {
            await using var ctx = await factory.CreateDbContextAsync();
            var record = IdempotencyRecord.Create("key-1", taskId, "hash", TimeSpan.FromHours(24));
            ctx.IdempotencyRecords.Add(record);
            await ctx.SaveChangesAsync();
        }

        IdempotencyRecord? found;
        using (accessor.SetTenant(tenant.Id))
        {
            var repository = new IdempotencyRecordRepository(factory);
            found = await repository.GetByKeyAsync("key-1");
        }

        found.Should().NotBeNull();
        found!.TaskId.Should().Be(taskId);
    }
}
