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

## 2026-07-22 — Attempt 3: MaxTokens truncation fix verified; a second, more serious defect found

`AgentOptions.MaxTokens["Orchestrator"]` was raised from 1024 to 4096
(`src/OrchestAI.API/appsettings.json:42`, commit `7930614`) — a pure configuration change, no
orchestration engine, agent behavior, or workflow logic touched. Full test suite reverified at
428/428 (unchanged) before and after the change.

A third live, full-cost run (Anthropic + Perplexity) confirmed the fix and re-confirmed both of
Attempt 2's positive results: agent selection was again exactly `Data`+`Research`, and all 33
live Perplexity queries this run stayed correctly scoped to the 2017-18 window (0/33 drift).
`finalResult` no longer truncates — the synthesis stage's `outputTokens` landed at 760, well
under the new 4096 cap, ending on a complete, natural sentence. `totalCostUsd: 0.089367`,
`totalInputTokens: 79599`, `totalOutputTokens: 6422`.

**However, this run could not answer the deeper question it was meant to test** (whether the
reconciliation stage explains *why* the two evidence streams aren't in conflict, versus merely
juxtaposing them) — because the contextual evidence never reached the reconciliation stage
intact. This surfaced a second defect, independent of the MaxTokens issue and more serious than
a cosmetic gap:

**The defect:** `AgentBase.cs:22` hardcodes `MaxAgenticIterations = 10` — a per-turn cap on any
agent's tool-use loop, shared by every agent type, unrelated to `MaxToolCallsPerTask` (the
already-correctly-designed cross-agent task-wide budget from ADR-015). This run's `Research`
agent needed 33 real Perplexity calls (vs. Attempt 2's 16 — real, expected variance in how much
digging a given case requires) and hit the 10-turn cap mid-tool-call. The loop discarded the
just-fetched results and exited, leaving whatever incidental "let me search more" aside the
model had appended to its *prior* turn as the agent's persisted `outputResult` — the task still
reported `status: Completed`, with no error, warning, or `RejectionEvent` anywhere in the
execution record. The Orchestrator's reconciliation stage correctly detected the input was
broken and declined to fabricate a synthesis, but that means the specific behavior this run was
designed to probe (explanation vs. juxtaposition) remains genuinely untested.

**This is not filed alongside the SQL-dialect self-correction noise as a low-priority,
indefinitely-deferred item.** It is a materially more serious finding, for a specific, precedented
reason: this project already established and enforced, in Week 11 / ADR-015 Confirmation #7,
that hitting an orchestrator-level cap must **reject the task cleanly — `Failed` status, a
specific error message, a `RejectionEvent`** — never silently truncate a plan or drop a result
while reporting success, "because a partially-executed task that looks `Completed`... is a
worse failure mode than a task that's visibly `Failed`" (quoted verbatim from that confirmation).
`MaxAgenticIterations` hitting its cap mid-tool-call and persisting a placeholder sentence as a
real answer — with the task still marked `Completed` — is exactly that anti-pattern, one layer
down (a per-agent turn budget instead of a per-task tool-call budget) that had simply never been
stress-tested with an agent needing more than 10 turns until this run happened to need 33.

Consistent with that same precedent, **the correct fix is not "raise `MaxAgenticIterations`"** —
raising a hard cap only postpones the identical silent-success failure at a higher number, the
same way raising `MaxTokens` would have only postponed the truncation bug rather than fixing why
a hard cutoff mid-truncates instead of failing cleanly. The right fix is: when an agent's
tool-use loop is exhausted while still mid-turn (i.e., the model just requested more tool calls
and got no chance to synthesize a final answer), the execution should be marked failed with a
real, observable error — not silently persisted as a successful completion with placeholder
text. This is a correctness/observability gap across every agent type, not something specific to
the Sports workflow, and it deserves its own narrow follow-up task before this MVP is genuinely
demo-ready — the same tier of seriousness as the (now-fixed) truncation bug, for the same
underlying reason: an artifact that silently looks successful while actually being broken
undermines the entire evidentiary premise this demo depends on.

No further live run was attempted after this finding. Attempt 3 needed roughly double Attempt
2's Perplexity queries (33 vs. 16) purely from case-to-case variance in how much digging the
research agent judges necessary — meaning a fourth attempt hitting the same 10-turn wall is
plausible, not unlikely, and spending another real-cost run on that coin flip would produce no
new information if it failed again the same way. This does not change Sports's classification
as a working, honestly-buildable candidate (the underlying facts a correctly-synthesizing
Research agent would have surfaced are the same facts Attempt 2 already reconciled
successfully) — it identifies a real, load-bearing implementation gap to close, separately from
domain selection, before relying on this MVP as evidence in front of Track B.
