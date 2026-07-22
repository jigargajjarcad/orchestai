# Phase 3 Domain Notes

Durable checkpoint for Phase 3 domain-selection reasoning. Not an implementation plan and not
an ADR — this records evaluation of candidate Phase 3 workflows against the current
orchestration architecture, pending final domain lock. See `docs/superpowers/plans/` for
locked implementation plans and `DECISIONS.md` for architecture decision records.

## 2026-07-21 — Phase 3 domain architecture-fit checkpoint

The following candidate workflows were evaluated against the current OrchestAI orchestration
architecture:

- Software Incident Investigation
- Cybersecurity Investigation
- Sports / Athlete Performance Investigation

The architectural-fit test was whether the workflow's central value could be demonstrated
using OrchestAI's existing fixed parallel/sequential orchestration model, without modifying
the core orchestration engine or redesigning the workflow into something fundamentally
different.

**Incident Investigation** was eliminated because its defining workflow requires
runtime-dependent agent selection: an initial investigation discovers evidence, and that
runtime finding determines which specialized agent investigates next. The current engine
fixes `ExecutionOrder` before sub-agents execute and does not support runtime-dependent agent
selection or branching.

**Cybersecurity Investigation** was independently evaluated and eliminated for the same
architectural reason. Its defining workflow requires findings from an earlier investigation to
determine what should be investigated next, followed by iterative evidence gathering to
validate or challenge the resulting hypothesis. A fixed upfront roster followed by one-shot
synthesis would be technically possible, but would remove the workflow's defining adaptive
investigation mechanic and therefore constitute a replacement workflow rather than a
meaningful compromise.

**Sports / Athlete Performance** remains the current working candidate because its proposed
workflow maps naturally onto the existing orchestration model: independent research/
investigation can execute in parallel or a fixed sequence, followed by a synthesis/validation
stage that consumes the completed outputs. The workflow does not require runtime-dependent
selection of the next agent. Its value can therefore be demonstrated without changing
OrchestAI's core architecture.

### Status

- Sports is a working candidate, not a formally locked Phase 3 domain.
- Final domain selection remains pending Track B external human feedback.
- Track B feedback should be gathered using the actual Phase 1 + Phase 2 system as it exists
  today, without presenting Sports or any other proposed domain as the predetermined answer.
- No Phase 3 planning or implementation should begin until Track B feedback has been gathered
  and the domain/workflow is formally locked.

## 2026-07-22 — Sports MVP feasibility: live-verified

Built and live-ran the smallest honest instantiation of the Sports/Athlete Performance
Investigation workflow: one real, cited athlete performance window (Kawhi Leonard's Dec 12,
2017 – Jan 13, 2018 return-from-injury stretch with the Spurs; see
`scripts/seed-sports-demo-data.sql` for sources), two existing agent types (`Data`, `Research`)
dispatched in parallel, reconciled by the existing, already-mandatory `ReviewAsync` stage —
zero orchestration-engine changes. See `docs/superpowers/plans/2026-07-21-phase3-sports-mvp.md`
for the full task-by-task record and `docs/phase3-sports-demo-runbook.md` for how to reproduce
it.

**Live verification took two attempts, not one.** Attempt 1 was a real, full-cost run
(Anthropic + Perplexity) that completed end-to-end (`status: Completed`) but surfaced three
real gaps, none of them architectural:

1. The runbook's plain, unauthenticated `POST /api/v1/tasks` call 401'd under the Week
   10/ADR-014 tenant-isolation middleware, which post-dates when the runbook was written. Had
   to reverse-engineer the admin bootstrap flow (mint a tenant + API key via
   `/api/v1/admin/*`) from `TenantAuthenticationMiddleware` source to proceed.
2. The `userPrompt` said only "last 9 games" with no explicit historical date range, so the
   orchestrator's generated Research sub-prompt anchored on "recent" instead of 2017-18 — all 8
   live Perplexity queries researched the wrong season entirely (Kawhi Leonard's 2024-25
   Clippers season instead of his 2017-18 Spurs stretch).
3. The orchestrator selected a third agent (`Writer`) alongside `Data`/`Research`, which ran in
   genuine parallel with them (not after, despite the plan's own `execution_order` implying
   sequencing) and so had no access to their outputs — a wasted, contentless execution.

Notably, the unmodified `ReviewAsync` reconciliation stage caught and explicitly flagged the
season mismatch from finding 2 rather than blindly merging the mismatched data — the
architecture's synthesis/validation stage worked correctly even while being fed mis-targeted
research.

All three gaps were fixed in the runbook only, not the orchestration engine (commit
`dac4003`): documented the admin auth bootstrap as a new Step 0, made the `userPrompt`
explicit about the Dec 12, 2017 – Jan 13, 2018 window, and noted the Kestrel port-override
caveat. Attempt 2 (also a real, full-cost run) verified the fix: all 16 live Perplexity queries
this time correctly targeted the 2017-18 season (0/16 drifted to 2024-25, vs. 0/8 correct in
attempt 1), and the orchestrator selected exactly `Data`+`Research` with no `Writer` — this
appears to be a side effect of the clearer, date-anchored prompt rather than a targeted fix for
finding 3, since nothing in the runbook fix specifically targeted agent selection.

**Final live result (Attempt 2):** `status: Completed`, `totalCostUsd: 0.070954`,
`totalInputTokens: 48242`, `totalOutputTokens: 8090`. With research correctly targeted this
time, the reconciliation stage produced a substantively real analysis rather than a
mismatch-flag: it identified a genuine contradiction (Leonard's minutes and points were
trending steadily upward through Game 7, then the Spurs announced an indefinite shutdown just
4 days later, citing that the quad injury "hasn't responded the way we wanted it to") and
attributed each claim explicitly back to statistical data vs. contextual research rather than
blending them into one unattributed narrative.

**Two new findings surfaced during Attempt 2, correctly left unfixed as out of this plan's
scope:**

1. `finalResult` truncates mid-sentence at exactly `outputTokens: 1024` in **both** attempts.
   This traces to `AgentOptions.MaxTokens["Orchestrator"] = 1024` in
   `src/OrchestAI.API/appsettings.json:42` — a systemic value used by every OrchestAI task's
   synthesis stage, not something specific to this workflow, so it was not touched here. Worth
   a dedicated follow-up task (raise the cap, or have the synthesis stage request
   continuation/summarize instead of hard-truncating).
2. `db_query` needed more self-correction cycles on identifier casing in Attempt 2 (8 calls)
   than Attempt 1 (3 calls), including one new SQL-Server-dialect miss (`SELECT TOP N` against
   Postgres). Not a regression — the Data agent self-corrects unaided every time in both
   attempts — but worth naming as noise for anyone tuning `DataAgent`'s system prompt for
   SQL-dialect awareness later.

This does not lock Sports as the Phase 3 domain — Track B external human feedback is still
outstanding (see the 2026-07-21 checkpoint above). It confirms the workflow is honestly
buildable as scoped, with no architectural blocker found; the gaps found were runbook/prompt
issues fixed without touching the orchestration engine, plus two named, unresolved,
out-of-scope findings for future follow-up.
