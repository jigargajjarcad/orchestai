using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class RejectionEventTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var apiKeyId = Guid.NewGuid();
        var evt = RejectionEvent.Create(RejectionReason.ConcurrencyExceeded, "req-1", "trace-1", apiKeyId, """{"limit":5}""");

        evt.Reason.Should().Be(RejectionReason.ConcurrencyExceeded);
        evt.RequestId.Should().Be("req-1");
        evt.TraceId.Should().Be("trace-1");
        evt.ApiKeyId.Should().Be(apiKeyId);
        evt.DetailsJson.Should().Be("""{"limit":5}""");
        evt.OccurredAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Create_NoTraceOrApiKey_AllowsNulls()
    {
        var evt = RejectionEvent.Create(RejectionReason.RateLimited, "req-2", null, null, "{}");

        evt.TraceId.Should().BeNull();
        evt.ApiKeyId.Should().BeNull();
    }
}
