using FluentAssertions;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AsyncLocalCurrentTenantAccessorTests
{
    [Fact]
    public void TenantId_NoScopeSet_IsNull()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();

        accessor.TenantId.Should().BeNull();
    }

    [Fact]
    public void SetTenant_WithinScope_ExposesTenantId()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var tenantId = Guid.NewGuid();

        using (accessor.SetTenant(tenantId))
        {
            accessor.TenantId.Should().Be(tenantId);
        }
    }

    [Fact]
    public void SetTenant_DisposingScope_RestoresPreviousValue()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var outer = Guid.NewGuid();
        var inner = Guid.NewGuid();

        using (accessor.SetTenant(outer))
        {
            using (accessor.SetTenant(inner))
            {
                accessor.TenantId.Should().Be(inner);
            }
            accessor.TenantId.Should().Be(outer);
        }
        accessor.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task SetTenant_FlowsAcrossAsyncContinuations()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var tenantId = Guid.NewGuid();

        using (accessor.SetTenant(tenantId))
        {
            await Task.Delay(1);
            await Task.Yield();
            accessor.TenantId.Should().Be(tenantId, "AsyncLocal must survive await continuations within the same logical call chain");
        }
    }

    [Fact]
    public async Task SetTenant_DoesNotLeakAcrossConcurrentAsyncFlows()
    {
        var accessor = new AsyncLocalCurrentTenantAccessor();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var taskA = Task.Run(async () =>
        {
            using (accessor.SetTenant(tenantA))
            {
                await Task.Delay(20);
                return accessor.TenantId;
            }
        });
        var taskB = Task.Run(async () =>
        {
            using (accessor.SetTenant(tenantB))
            {
                await Task.Delay(10);
                return accessor.TenantId;
            }
        });

        var results = await Task.WhenAll(taskA, taskB);

        results[0].Should().Be(tenantA, "each Task.Run body has its own async flow and must not see the other's tenant");
        results[1].Should().Be(tenantB);
    }
}
