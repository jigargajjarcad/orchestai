# OrchestAI — Design Principles

Standing architectural conventions this project has followed since Week 7, written down once so
future weekly specs can reference them directly instead of re-explaining them each time.

## Enterprise-first, not demo-first
Every feature is built to the standard a real paying customer's data would demand — no "good
enough for a portfolio demo" shortcuts on auth, isolation, or data integrity. Week 10's tenant
isolation work is the clearest expression of this: it was treated as a security boundary, not a
feature, from the first line of its spec.

## Observability and security are defaults, not add-ons
Every agent execution, tool call, and cost event has been traceable since Week 7 (`ADR-011`); every
tenant-scoped table has been isolated since Week 10 (`ADR-014`). Neither was bolted on after the
fact — both were designed to apply automatically to new tables/entities as the system grows,
rather than requiring a developer to remember to add tracing or isolation to each new feature.

## Single-choke-point enforcement for cross-cutting concerns
When a concern applies to many tables/queries at once (cost segregation by `Source` — `ADR-012`;
tenant isolation by `TenantId` — `ADR-014`), the enforcement lives in exactly one place (a
repository method's `Where` clause, a global EF query filter, a `SaveChanges` interceptor) that
every caller passes through, rather than being re-implemented at each call site where it could be
forgotten. If a review ever finds the same cross-cutting check duplicated in multiple handlers,
that's a signal the centralization isn't working as designed, not a normal implementation detail.

## Fail closed, not fail open
Missing tenant context denies access; it never falls back to a default/permissive state
(`ADR-014`). A missing regression baseline throws rather than returning a zeroed-out report
(`ADR-012`). When a system doesn't know the answer, the safe default is "no," not "yes, probably."

## Architectural boundaries are enforced by tests, not just by review
Clean Architecture layering (`Domain ← Application ← Infrastructure ← API`) is checked by
`LayeringTests`, which fail the build the moment a violation is introduced — it does not rely on
a reviewer noticing. This project has already needed to extend that guardrail twice for the same
underlying mistake: Week 9's `RequestPostHocScoringHandler` initially depended on
`Infrastructure.Configuration` from `Application` (caught by `Application_DoesNotDependOnInfrastructure`),
and Week 10's `RequireAdminSecretFilter`/`TenantAuthenticationMiddleware` were initially placed in
`Infrastructure` when both are ASP.NET Core pipeline glue (`IAsyncActionFilter`/`IMiddleware`)
that belongs in `API` — a violation the existing `Infrastructure_DoesNotDependOnApi` check
couldn't see, since neither type referenced `OrchestAI.API` directly, only
`Microsoft.AspNetCore.Mvc`/`Microsoft.AspNetCore.Http`. Rather than trust the next reviewer to
catch the same mistake a third time, two new checks
(`Infrastructure_DoesNotDependOnAspNetCoreMvc`, `Infrastructure_DoesNotDependOnAspNetCoreHttp`)
were added to close that specific detection gap permanently. A layering rule that only lives in a
person's memory of "where things go" is not actually enforced.

## Empirical verification over plausible-sounding review
A diff that reads correctly is not the same as a diff that behaves correctly at runtime — ASP.NET
Core routing bugs, DI captive-dependency issues, and Docker build corruption have all been caught
in this project only by actually running the app, not by review alone. Week 10 added two more
examples of this same pattern: `TenantScopingInterceptor`'s `IsModified`-based tamper check looked
correct on paper but would have rejected every legitimate status update in production under this
codebase's disconnected-`Update()` repository pattern, and whether EF Core's `ExecuteDelete`
honors a global query filter was proven against a real Postgres instance rather than assumed from
documentation. Claims about test counts, migration correctness, or isolation behavior are verified
by running the relevant command and reading its real output, not by reasoning about what "should"
happen.

## Decide, and write down why — including what's deliberately deferred
Every ADR in `DECISIONS.md` records not just what was decided but why, and explicitly lists what
was NOT decided yet and what would trigger revisiting it (e.g. retention policy, baseline
auto-selection, rate limiting). Guessing at a policy with no real usage data to inform it is
treated as worse than leaving it explicitly open.

## Reuse before rebuild
When an existing mechanism already solves a new problem's shape (`EvalCase.CreateEphemeral`
reusing `IEvalScorer` unchanged for post-hoc scoring; `TenantScopingInterceptor` mirroring
`UpdatedAtInterceptor`'s exact shape), extend or reuse it rather than introducing a parallel
abstraction that could drift from the original over time.

## Operational state vs. audit state
Separate transient operational state from durable audit state. Operational state exists only to
support the system's current behavior (reservations, rate-limiter counters, concurrency slots,
queue depth, caches). It is ephemeral, may be reconstructed or discarded after failures, and
should never become part of the permanent historical record. Audit state records what actually
happened during execution (traces, spans, immutable cost ledger entries, evaluation results).
Audit state is immutable, durable, and must never be rewritten or derived from operational state.
This principle was implicit in the design decisions made during Weeks 7-10 (the cost ledger has
always been the append-only source of truth that `CostRollup` derives from, never the reverse) and
is now made explicit as a standing architectural rule for future development. Every new feature
should ask: Is this operational state or audit state? Does this belong in the immutable record?
Can losing this state after a crash affect history, or only future behavior?
