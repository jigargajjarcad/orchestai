# Phase 3 — Sports/Athlete Performance Investigation MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and live-verify the smallest honest instantiation of the Sports/Athlete Performance Investigation workflow — one athlete, one bounded real game window, two existing agent types running in parallel, and OrchestAI's already-mandatory synthesis stage producing a traceable, evidence-backed conclusion — using zero changes to the orchestration engine, `AgentBase`, or any Weeks 1-12 architecture.

**Architecture:** No core code changes. This adds exactly one new database table (schema-only migration, no EF entity — queried entirely through the existing `db_query`/`DatabaseTool` raw-SQL path), one seed script populating it with verified real public data, a short runbook describing the exact task submission that exercises the workflow through the existing `OrchestAI.API`, and a findings addendum to `docs/phase3-domain-notes.md`. The workflow's "independent investigation → reconcile → traceable report" shape maps directly onto the engine's existing, already-mandatory two-phase execution shape: `StartOrchestrationHandler` dispatches the orchestrator-selected sub-agents (`StartOrchestrationHandler.cs:158-175`), then unconditionally calls `OrchestratorAgent.ReviewAsync` afterward (`StartOrchestrationHandler.cs:183-185`), which already receives every prior agent's output pre-labeled by agent type (`OrchestratorAgent.cs:182-183`) for traceability.

**Tech Stack:** Existing stack only — EF Core migration (raw SQL, no new entity), a `.sql` seed script, the already-running `OrchestAI.API`, `WebSearch`/`WebFetch` (used only to source and cite real public sports data for the seed script, not for any orchestration-time capability).

## Global Constraints

- No changes to `OrchestAI.Domain`, `OrchestAI.Application`, or `OrchestAI.Infrastructure`'s existing files, `AgentBase`, `OrchestratorAgent`, `StartOrchestrationHandler`, or any orchestration/branching logic. The only new Infrastructure content is one additive migration (a new table, not a change to any existing table).
- No new `AgentType`. This MVP uses exactly two existing agent types (`Data`, `Research`) — adding a second bespoke agent type is the specific scope-creep trap named in this plan's own investigation (see below) and is explicitly out of scope.
- The seeded data must be **real, publicly-verifiable sports statistics for a real athlete**, not fictional data — fictional data was considered and rejected during investigation because it makes `ResearchAgent`'s contextual leg (whether live-cited via Firecrawl/Perplexity or drawn from the model's own trained knowledge) meaningless: there is nothing real to find about a made-up person. Every seeded row must carry a `SourceUrl` citing where its numbers came from.
- Firecrawl/Perplexity credentials remain optional and are not required for this workflow to demonstrate its value — `ResearchAgent`'s output must be honestly framed (in the demo runbook and any resulting report) as live-and-cited when keys are configured, model-knowledge-only when they are not. Do not silently treat degraded output as equivalent to cited research.
- `db_query` is read-only by construction (`DatabaseTool.cs:81-86` rejects `INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|TRUNCATE|EXEC|EXECUTE` via regex) — the seed script must run directly against Postgres (`psql`/`ExecuteSqlRawAsync` in a throwaway script), never through the `db_query` tool itself.
- No PR-based workflow — same worktree → local merge → push pattern as every prior phase.
- This plan does not lock the Sports domain. Track B (external human feedback) remains outstanding regardless of this plan's outcome — see `docs/phase3-domain-notes.md`.

## Scope Estimate

For a solo builder: Task 1 (migration) ~30 min, Task 2 (sourcing + verifying real data) ~1-2 hours
(the least predictable step — finding a case with both clean box scores and a clear documented
context takes real search time), Task 3 (runbook) ~30 min, Task 4 (live run, likely 2-3 prompt
iterations to get agent selection and reconciliation quality right) ~1-2 hours, Task 5-6 ~30 min.
**Total: roughly half a day to a day**, most of it in Tasks 2 and 4, not in code.

**The most likely scope-creep trap:** once Task 2's research turns up one interesting real athlete/
stretch, the temptation is to seed multiple athletes, multiple stat types, or a general-purpose
"query any athlete" prompt instead of one fixed case — turning this from "one small, provably-
working demonstration" into "a sports stats platform." Resist it, the same way Phase 2 resisted
turning its disposable console consumer into a second sample app: one athlete, one window, one
fixed `userPrompt` shape is the deliverable, not a generalized capability.

---

### Task 1: Add the sports performance table (schema-only migration)

**Files:**
- Create: `src/OrchestAI.Infrastructure/Migrations/<timestamp>_AddSportsPerformanceGamesTable.cs` (timestamp auto-generated by `dotnet ef migrations add`)
- Modify: `src/OrchestAI.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (auto-updated by the same command — do not hand-edit)

**Interfaces:**
- Produces: a table `"SportsPerformanceGames"` queryable via the existing `db_query` tool (`database` parameter omitted or `"default"` — it lives in the same Postgres database as everything else). No C# entity, no `DbSet`, no repository — nothing else in this codebase needs to read this table via EF Core.

- [ ] **Step 1: Generate an empty migration**

Run (from repo root):
```bash
export PATH="$PATH:$HOME/.dotnet/tools"
dotnet ef migrations add AddSportsPerformanceGamesTable \
  --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API
```
Expected: a new migration file with empty `Up()`/`Down()` bodies (no entity was added to `AppDbContext`, so EF Core has nothing to diff).

- [ ] **Step 2: Fill in the migration's `Up()`/`Down()` methods**

Open the generated file and replace the body with:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("""
        CREATE TABLE "SportsPerformanceGames" (
            "Id" SERIAL PRIMARY KEY,
            "AthleteName" TEXT NOT NULL,
            "Sport" TEXT NOT NULL,
            "GameNumber" INT NOT NULL,
            "GameDate" DATE NOT NULL,
            "Opponent" TEXT NOT NULL,
            "IsHomeGame" BOOLEAN NOT NULL,
            "MinutesPlayed" INT NOT NULL,
            "Points" INT NOT NULL,
            "ReportedInjuryNote" TEXT NULL,
            "SourceUrl" TEXT NULL
        );
        """);
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("""DROP TABLE "SportsPerformanceGames";""");
}
```

- [ ] **Step 3: Apply the migration against a local Postgres and verify**

Run:
```bash
docker compose up -d postgres
export PATH="$PATH:$HOME/.dotnet/tools"
ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=orchestai;Username=orchestai;Password=changeme" \
Anthropic__ApiKey="dummy-for-migration-only" \
dotnet ef database update --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API
docker exec -it $(docker compose ps -q postgres) psql -U orchestai -d orchestai -c '\d "SportsPerformanceGames"'
```
Expected: the `\d` output shows all 11 columns with the exact types above.

- [ ] **Step 4: Run the existing full test suite to confirm nothing regressed**

Run: `dotnet test OrchestAI.sln`
Expected: same pass count as the pre-task baseline (this migration adds a table nothing else references — no existing test should be affected).

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Infrastructure/Migrations/
git commit -m "feat: add SportsPerformanceGames table for Phase 3 sports investigation MVP"
```

---

### Task 2: Source, verify, and seed real public performance data

**Context:** This is a research task, not a code-writing task — the specific athlete and numbers are deliberately not pre-supplied by this plan, because inventing "real-looking" statistics for a real, named person without verification would be dishonest and risky. Follow this process exactly; do not fabricate numbers.

**Files:**
- Create: `scripts/seed-sports-demo-data.sql`

- [ ] **Step 1: Find a real, well-documented case**

Using `WebSearch`/`WebFetch`, find a real professional athlete (any sport) with a **publicly documented, bounded stretch of games** (8-12 games) where:
- Box-score-level stats (minutes played, a primary counting stat like points) are publicly available per-game from a citable source (official league site, ESPN, Basketball-Reference/Baseball-Reference/Pro-Football-Reference or equivalent).
- There is a real, publicly reported contextual event overlapping that stretch that plausibly explains a real change in performance — e.g., a documented injury, a return-from-injury ramp-up, a documented role/minutes change. This is what gives the "reconcile contradictions" stage real content to work with.

Success criteria for this step: you can name the athlete, the exact date range, and cite at least one URL for the box scores and at least one URL for the contextual event.

- [ ] **Step 2: Verify internal consistency**

Cross-check the per-game numbers you'll seed against the cited source directly (re-fetch the box score page, don't work from memory of the search result). Every row's `Points`/`MinutesPlayed`/`Opponent`/`GameDate` must match the source exactly.

- [ ] **Step 3: Write the seed script**

```sql
-- Seed data for Phase 3 Sports Investigation MVP.
-- Source: <fill in the box-score source URL(s) from Step 1>
-- Context source: <fill in the contextual/injury-report source URL from Step 1>
INSERT INTO "SportsPerformanceGames"
    ("AthleteName", "Sport", "GameNumber", "GameDate", "Opponent", "IsHomeGame", "MinutesPlayed", "Points", "ReportedInjuryNote", "SourceUrl")
VALUES
    ('<athlete name>', '<sport>', 1, '<date>', '<opponent>', <true|false>, <minutes>, <points>, NULL, '<box score url>'),
    -- ... one row per game, 8-12 rows total ...
    ('<athlete name>', '<sport>', N, '<date>', '<opponent>', <true|false>, <minutes>, <points>, '<the real reported context, e.g. "Team injury report listed <athlete> as questionable with a hamstring issue">', '<context source url>');
```

Every row must carry a real `SourceUrl`. Only rows near/after the documented contextual event get a non-null `ReportedInjuryNote` — earlier rows should be `NULL`, so `DataAgent`'s own stats trend and the seeded context don't trivially give away the answer before any agent reasons over it.

- [ ] **Step 4: Run it and verify**

Run:
```bash
docker exec -i $(docker compose ps -q postgres) psql -U orchestai -d orchestai < scripts/seed-sports-demo-data.sql
docker exec -it $(docker compose ps -q postgres) psql -U orchestai -d orchestai -c 'SELECT "GameNumber", "Opponent", "Points", "ReportedInjuryNote" FROM "SportsPerformanceGames" ORDER BY "GameNumber";'
```
Expected: the exact rows written in Step 3, in order, matching the cited sources.

- [ ] **Step 5: Commit**

```bash
git add scripts/seed-sports-demo-data.sql
git commit -m "feat: seed real, cited performance data for Phase 3 sports investigation MVP"
```

---

### Task 3: Write the demo runbook

**Files:**
- Create: `docs/phase3-sports-demo-runbook.md`

**Context:** No new consumer app is needed here (unlike Phase 2) — this workflow runs through the already-working `OrchestAI.API`, exactly as every other task does. This runbook documents the exact `userPrompt` and API calls needed to exercise the workflow, so Task 4's live run is reproducible rather than ad hoc.

- [ ] **Step 1: Write the runbook**

```markdown
# Phase 3 Sports Investigation — Demo Runbook

Prerequisites: Postgres running and migrated (Task 1), seed data loaded (Task 2), `OrchestAI.API`
running locally with a real `Anthropic__ApiKey`.

## 1. Create the task

    POST /api/v1/tasks
    {
      "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "title": "Sports performance investigation",
      "userPrompt": "Investigate <athlete name>'s performance over their last <N> games (query the SportsPerformanceGames table for AthleteName = '<athlete name>', ordered by GameNumber). Independently: (1) analyze the raw performance trend in minutes played and points across these games, and (2) research any publicly reported context (injuries, role changes) around this stretch. Then explicitly compare the two: does the reported context explain the statistical trend, or is there a contradiction between what the numbers show and what the public context suggests? Produce a final conclusion that traces each claim back to whether it came from the statistical data or the contextual research."
    }

Note: `DevUserId` (`3fa85f64-5717-4562-b3fc-2c963f66afa6`, from `DatabaseSeeder.cs:9`) is the same
well-known dev user ID used throughout this project's demos.

## 2. Start it

    POST /api/v1/tasks/{id}/start

## 3. Read the result

    GET /api/v1/tasks/{id}

Check `finalResult` for the reconciled conclusion, and (optionally) `GET /api/v1/tasks/{id}?includeMessages=true&includeToolCalls=true`
to see each agent's actual `db_query`/`perplexity_search`/`firecrawl_scrape` calls for full traceability.

## What to check on each run

- Did the orchestrator's plan (visible via the `orchestrator_plan` SSE event, or `includeMessages=true`)
  select exactly `Data` and `Research` — not fewer, not more?
- Did `DataAgent` actually call `db_query` against `SportsPerformanceGames` (not hallucinate numbers)?
- Did the final result explicitly address agreement/contradiction between the two sources, citing
  which agent each claim came from?
- If Firecrawl/Perplexity keys are not configured, does `ResearchAgent`'s contribution get
  correctly labeled as unsourced model knowledge rather than presented as cited research?
```

- [ ] **Step 2: Commit**

```bash
git add docs/phase3-sports-demo-runbook.md
git commit -m "docs: add Phase 3 sports investigation demo runbook"
```

---

### Task 4: Live end-to-end run

**Context:** Same evidence standard as Phase 1 and Phase 2 — prove it, don't assert it. This is the step that turns "the workflow should work" into "it worked."

- [ ] **Step 1: Run the exact sequence from the runbook (Task 3) against a real `Anthropic__ApiKey`**

Capture the full request/response transcript for all three calls (create, start, read).

- [ ] **Step 2: Verify agent selection**

Fetch the task with `includeMessages=true` and confirm the orchestrator's plan selected exactly `Data` and `Research`. If it selected something else (or only one agent), that's a prompt-engineering finding to fix by revising the `userPrompt` in the runbook (Task 3) — not a code change, and not evidence the workflow doesn't fit the architecture; note it explicitly either way.

- [ ] **Step 3: Verify traceability and reconciliation**

Read `finalResult`. Confirm it references both the statistical trend and the contextual finding, and states whether they agree or conflict. If it doesn't, this is the point to determine whether the gap is prompt quality (fixable by revising the `userPrompt` or the athlete/window chosen in Task 2) or something deeper — if deeper, stop and report it rather than silently iterating indefinitely.

- [ ] **Step 4: Record the real cost/token numbers and the real final text, verbatim, for the findings writeup (Task 5)**

- [ ] **Step 5: Tear down**

`docker compose down` once verification is captured.

---

### Task 5: Record findings in `docs/phase3-domain-notes.md`

**Files:**
- Modify: `docs/phase3-domain-notes.md` (append a new dated section — do not edit the existing 2026-07-21 checkpoint)

- [ ] **Step 1: Append a dated findings section**

```markdown
## <run date> — Sports MVP feasibility: live-verified

Built and live-ran the smallest honest instantiation of the Sports/Athlete Performance
Investigation workflow: one real, cited athlete performance window (see
`scripts/seed-sports-demo-data.sql` for sources), two existing agent types (`Data`, `Research`)
dispatched in parallel, reconciled by the existing, already-mandatory `ReviewAsync` stage —
zero orchestration-engine changes. See `docs/superpowers/plans/2026-07-21-phase3-sports-mvp.md`
for the full task-by-task record and `docs/phase3-sports-demo-runbook.md` for how to reproduce it.

Live result: <paste the actual final result text, cost, and token counts from Task 4>.

This does not lock Sports as the Phase 3 domain — Track B external human feedback is still
outstanding (see the 2026-07-21 checkpoint above). It confirms the workflow is honestly
buildable as scoped, with no architectural blocker found.
```

- [ ] **Step 2: Commit**

```bash
git add docs/phase3-domain-notes.md
git commit -m "docs: record Phase 3 sports MVP live-verification findings"
```

---

### Task 6: Finish the branch

- [ ] **Step 1: Run the full test suite once more**

Run: `dotnet test OrchestAI.sln`
Expected: same pass count as the pre-task baseline — this plan adds no code to any tested project (Domain/Application/Infrastructure/API), only a new table, a seed script, and docs.

- [ ] **Step 2: Use `superpowers:finishing-a-development-branch`**

Follow that skill's structured options (merge to `main` locally, per this project's standing
"no PR-based workflow" pattern used in every prior phase).
