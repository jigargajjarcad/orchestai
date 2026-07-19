# RUNBOOK

Operational recovery guidance for OrchestAI's production deployment (Railway, single API instance +
managed PostgreSQL). Read `ADR-016` (`DECISIONS.md`) for the full reasoning behind every policy
referenced here.

## Incident classification

Start here. Pick the branch that matches what you're actually observing.

| Symptom | Classification | Action |
|---|---|---|
| App was working, a recent deploy broke it, `/health/ready` failing or requests erroring | **Operational failure** | Redeploy the previous successful build/image (see "Rollback: redeploy the previous build" below). |
| `/health/ready` returns `503` with `reason: "database unreachable"`, `/health/live` still `200` | **Infrastructure failure** | Restart/verify the Postgres instance in Railway's dashboard; once reachable, confirm `/health/ready` returns to `200` before assuming recovery. |
| A migration was just deployed and something looks wrong (data-shape errors, constraint violations) | **Migration failure** | Follow "Rollback: migration-aware" below — do **not** just redeploy the previous image without checking schema compatibility first. |
| App crashes immediately, or logs show `Required configuration is missing or blank` at startup | **Configuration failure** | Restore the previous Railway environment variables (compare against `.env.example`); this is exactly what `RequiredConfigurationValidator` (ADR-016 confirmation #8) is designed to make loud and immediate rather than a mysterious runtime crash. |

**A startup-time DB outage is the "Infrastructure failure" row above, not a new incident.** If the
database is unreachable at container boot, `Program.cs` checks `Database.CanConnectAsync()` before
attempting migration (ADR-016 Confirmation #2, fixed in Task 3, commit `4596e07`). When that check
fails, migration/seeding is skipped (`Log.Warning`, not thrown) and the app continues starting
normally: Kestrel binds, `/health/live` stays unconditionally `200`, and `/health/ready` reports
`503 "database unreachable"` until the database recovers. **Expect "live, not ready" during a
startup-time DB outage — not a crash loop.** This is documented, intended behavior, not something to
investigate from scratch.

This is deliberately distinct from a genuine migration failure: if the database **is** reachable but
`MigrateAsync()` itself then throws (a real migration bug — bad SQL, a broken `Down()`/`Up()`), that
exception is **not** caught by the connectivity check above. It propagates to the outer fail-fast
catch and crashes the process loudly (`Log.Fatal`, `Environment.ExitCode = 1`), exactly as any other
startup failure — that's the "Migration failure" row, handled via the migration-aware rollback below,
not a database-connectivity issue.

## Rollback: redeploy the previous build

Railway's redeploy-previous-build feature is the rollback mechanism for this project — there is no
blue-green/canary deployment strategy (deliberately out of scope, see ADR-016). In the Railway
dashboard: **Deployments → find the last known-good deployment → Redeploy**. This only works safely
because of the migration-compatibility rule below — if it doesn't hold for the deploy you're rolling
back past, do the migration-aware rollback instead.

## The migration-compatibility rule that makes rollback safe

**Schema changes must remain backward-compatible with the immediately-prior application version.**
A rollback redeploys old *code* against whatever schema is currently live — it does not touch the
database. If a migration in the deploy being rolled back is not backward-compatible with the
previous code version, redeploying that previous code against the now-current schema will break it.

For anything that isn't purely additive (a new nullable column, a new table, a new index), follow
the same multi-step pattern Week 10 already established for the tenant-isolation rollout:
1. Add the new column/constraint as **nullable**, deploy.
2. Backfill data in a separate step/deploy.
3. Only once backfilled, deploy the migration that makes it **non-null**/enforced.

Never ship a single breaking migration that would strand a rolled-back deployment against a schema
its code doesn't understand.

## Migration reversibility policy (ADR-016 confirmation #7)

Every migration's `Down()` either performs real, working rollback work (purely additive changes) or
throws `NotSupportedException` with a documented reason (irreversible changes — data transformations,
destructive operations). Enforced by `MigrationReversibilityTests`
(`tests/OrchestAI.Tests/Architecture/`).

**Production rollback does not mean running `dotnet ef database update <previous-migration>`
against the live database.** `Down()` existing and working is a local-development/testing
convenience (letting a developer cleanly undo a migration on their own machine), not a production
recovery mechanism. Production recovery is *always* "redeploy the previous application version
against a schema that remains compatible with it" (see above) — never an automatic downgrade
executed against live data.

## Known Limitations

Consolidated from ADR-011 through ADR-016 — check this list before treating one of these as a new
incident:

- **Rate-limiter bucket immutability after a live limit change** (ADR-015 confirmation #1 /
  implementation note). An admin `PUT .../limits` call that changes a tenant's `RequestsPerMinute`
  has zero effect on that tenant's *already-created* in-memory rate-limiter bucket until the process
  restarts (which resets every tenant's buckets, not just the changed one). Not a bug if observed —
  it's a named, accepted limitation of `System.Threading.RateLimiting`'s partition-caching model.
  Planned fix (not yet built): partition-key versioning (`{tenantId}:{limitsVersion}`).
- **Single-instance architecture, no distributed rate limiter.** The token-bucket rate limiter, the
  per-task tool-call budget counter, and the per-tenant queue-depth counter all live in single-
  process memory (ADR-015 confirmation #2). A second concurrently-running API instance would let a
  tenant's requests/tool calls/queue depth spread across instances that don't share state, silently
  defeating each limit. Deliberately out of scope until a second instance is actually deployed.
- **In-memory reservations, TTL-based crash recovery.** `TaskAdmissionReservation` rows are pure
  operational state (ADR-015 confirmation #5, `DESIGN_PRINCIPLES.md`'s "Operational state vs. audit
  state"). A reservation whose owning task crashes mid-execution is never explicitly released — it
  physically remains in the table until it ages out of admission math past
  `AbuseProtectionOptions.ReservationStalenessMinutes` (default 30 minutes). No reconciliation sweep
  deletes these orphaned rows; if they become numerous enough to matter, that's the trigger to build
  one (not yet needed).
- **`/health/ready`'s migration check is a live drift detector, not a startup gate** (ADR-016
  confirmation #2). Since `Program.cs` always auto-migrates at startup, immediately-post-startup this
  check is always trivially "no pending migrations." It only becomes meaningful if the schema drifts
  out from under a running container after the fact (e.g. a manual production DB change) — expected
  behavior, not a bug, if you ever see it fire on a container that's been running for a while.
