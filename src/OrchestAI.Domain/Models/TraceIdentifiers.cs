using System.Diagnostics;

namespace OrchestAI.Domain.Models;

// Generates OpenTelemetry-shaped trace/span identifiers — see ADR-011 for why this
// matters (a future OTLP exporter is a mapping exercise, not a redesign).
public static class TraceIdentifiers
{
    public static string NewTraceId() => ActivityTraceId.CreateRandom().ToHexString();

    public static string NewSpanId() => ActivitySpanId.CreateRandom().ToHexString();
}
