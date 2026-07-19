using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using OrchestAI.Infrastructure.Tenancy;

namespace OrchestAI.Tests.Infrastructure;

public sealed class InMemoryTaskStreamTicketIssuerTests
{
    private static InMemoryTaskStreamTicketIssuer CreateIssuer(TimeSpan? ttl = null) =>
        new(new MemoryCache(new MemoryCacheOptions()), ttl ?? TimeSpan.FromSeconds(60));

    [Fact]
    public void Issue_ThenTryConsume_WithCorrectTaskId_SucceedsAndReturnsTenantId()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var ticket = issuer.Issue(tenantId, taskId);
        var consumed = issuer.TryConsume(ticket, taskId, out var resolvedTenantId);

        consumed.Should().BeTrue();
        resolvedTenantId.Should().Be(tenantId);
    }

    [Fact]
    public void TryConsume_SameTicketTwice_SecondAttemptFails_SingleUse()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var ticket = issuer.Issue(tenantId, taskId);

        var first = issuer.TryConsume(ticket, taskId, out _);
        var second = issuer.TryConsume(ticket, taskId, out var secondTenantId);

        first.Should().BeTrue();
        second.Should().BeFalse("a ticket must be consumed exactly once, even within its TTL window");
        secondTenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryConsume_WrongTaskId_Fails_EvenBeforeExpiry()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var taskIdMintedFor = Guid.NewGuid();
        var differentTaskId = Guid.NewGuid();
        var ticket = issuer.Issue(tenantId, taskIdMintedFor);

        var consumed = issuer.TryConsume(ticket, differentTaskId, out var resolvedTenantId);

        consumed.Should().BeFalse(
            "a ticket minted for task X must never validate against task Y's stream, even for the same tenant");
        resolvedTenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task TryConsume_AfterTtlElapsed_Fails()
    {
        var issuer = CreateIssuer(TimeSpan.FromMilliseconds(200));
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var ticket = issuer.Issue(tenantId, taskId);

        await Task.Delay(TimeSpan.FromMilliseconds(600));

        var consumed = issuer.TryConsume(ticket, taskId, out var resolvedTenantId);

        consumed.Should().BeFalse("an expired ticket must never validate, regardless of taskId correctness");
        resolvedTenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryConsume_UnknownTicket_Fails()
    {
        var issuer = CreateIssuer();

        var consumed = issuer.TryConsume("not-a-real-ticket", Guid.NewGuid(), out var resolvedTenantId);

        consumed.Should().BeFalse();
        resolvedTenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void TryConsume_NullOrEmptyTicket_Fails()
    {
        var issuer = CreateIssuer();

        issuer.TryConsume(null, Guid.NewGuid(), out _).Should().BeFalse();
        issuer.TryConsume(string.Empty, Guid.NewGuid(), out _).Should().BeFalse();
    }

    [Fact]
    public void Issue_ProducesDistinctUnpredictableTickets()
    {
        var issuer = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        var ticketA = issuer.Issue(tenantId, taskId);
        var ticketB = issuer.Issue(tenantId, taskId);

        ticketA.Should().NotBe(ticketB);
        ticketA.Length.Should().BeGreaterThanOrEqualTo(32, "the ticket must be cryptographically random, not a guessable/short value");
    }
}
