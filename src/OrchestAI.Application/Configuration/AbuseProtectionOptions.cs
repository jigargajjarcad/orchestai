namespace OrchestAI.Application.Configuration;

// System-wide, non-per-tenant abuse-protection knobs (distinct from TenantLimitsDefaults,
// which are per-tenant-overridable). See ADR-015.
public sealed class AbuseProtectionOptions
{
    public const string SectionName = "AbuseProtection";

    // Crash-recovery TTL for TaskAdmissionReservation rows — see DESIGN_PRINCIPLES.md
    // "Operational state vs. audit state" and ADR-015. Must exceed the longest expected
    // orchestration duration plus a safety margin.
    public int ReservationStalenessMinutes { get; init; } = 30;

    public int IdempotencyKeyTtlHours { get; init; } = 24;

    // How long EfTenantLimitsProvider.GetAsync caches a resolved TenantLimits row before
    // re-reading the database. GetSnapshot never re-reads regardless of this value — it is
    // cache-only by construction.
    public int TenantLimitsCacheRefreshSeconds { get; init; } = 30;

    // IBudgetEstimator's conservative per-tool-call cost assumption — deliberately a flat,
    // over-conservative number rather than a real pricing lookup. See ADR-015 confirmation #8.
    public decimal AssumedCostPerToolCallUsd { get; init; } = 0.05m;
}
