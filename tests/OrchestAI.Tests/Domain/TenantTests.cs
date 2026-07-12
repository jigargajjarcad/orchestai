using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class TenantTests
{
    [Fact]
    public void Create_StartsActive()
    {
        var tenant = Tenant.Create("Acme Corp", "acme-corp");

        tenant.Name.Should().Be("Acme Corp");
        tenant.Slug.Should().Be("acme-corp");
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.SuspendedAt.Should().BeNull();
    }

    [Fact]
    public void Suspend_SetsStatusAndTimestamp()
    {
        var tenant = Tenant.Create("Acme Corp", "acme-corp");

        tenant.Suspend();

        tenant.Status.Should().Be(TenantStatus.Suspended);
        tenant.SuspendedAt.Should().NotBeNull();
    }

    [Fact]
    public void Reactivate_ClearsSuspension()
    {
        var tenant = Tenant.Create("Acme Corp", "acme-corp");
        tenant.Suspend();

        tenant.Reactivate();

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.SuspendedAt.Should().BeNull();
    }
}
