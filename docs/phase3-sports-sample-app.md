# OrchestAI Sample: Sports/Athlete Performance Investigation

This is OrchestAI's Phase 3 sample application — a small, deliberately-bounded demonstration
of what the framework's multi-agent orchestration actually buys you, built on top of the
already-shipped Weeks 1–12 engine (see the main [README](../README.md) for the full framework).
For the technical verification steps behind this sample (auth bootstrap, exact API calls,
what-to-check checklist), see [docs/phase3-sports-demo-runbook.md](phase3-sports-demo-runbook.md) —
this document is the narrative companion to that runbook, not a replacement for it.

## What OrchestAI is, in one sentence

A production-grade C#/.NET multi-agent orchestration framework: submit a task, an Orchestrator
agent decomposes it and dispatches specialist agents (in parallel or in sequence), and a
synthesis stage reconciles their outputs into one traceable result — all authenticated,
rate-limited, cost-tracked, and inspectable after the fact.

## What this specific sample demonstrates

**The question being investigated:** Did Kawhi Leonard's on-court performance, across his
December 12, 2017 – January 13, 2018 return-from-injury stretch with the San Antonio Spurs,
actually reflect his real health status? Two independent agents investigate this from
completely different evidence sources, and a third stage reconciles what they each found.

**The defensible claim this makes — read carefully:** this sample does **not** claim that a
single LLM call couldn't produce a similarly-worded final answer. It could. What OrchestAI
actually demonstrates is something a single call can't show you: **independent evidence paths,
gathered through separately-executed, separately-auditable agents, with their comparison and
reconciliation made explicit and inspectable** — not just a fluent-sounding answer, but a visible
record of what each path found, where they agreed or disagreed, and how the final conclusion was
actually reached from that evidence.

## How the independent evidence paths work

Two agents run **in parallel**, each with a different responsibility and a different evidence
source, and neither sees the other's work while running:

- **`DataAgent`** queries a seeded, cited Postgres table (`SportsPerformanceGames` — real
  box-score numbers for all 9 games in the window, each row sourced to a real ESPN box-score
  URL) via the `db_query` tool. This is the **statistical** evidence path: minutes played,
  points scored, game-by-game, with no interpretation.
- **`ResearchAgent`** independently searches the live web via the `perplexity_search` tool for
  publicly-reported context from the same window — injury status, medical reporting, the
  eventual season-ending shutdown announcement. This is the **contextual** evidence path.

Both agents' outputs — including every individual tool call they made — are recorded and
attributable after the fact. Nothing about one agent's investigation influences what the other
one looks for; they are structurally independent.

## How reconciliation works

Once both agents finish, OrchestAI's existing `OrchestratorAgent.ReviewAsync` stage — the same
synthesis step every OrchestAI task already goes through, unmodified for this sample — receives
both agents' full outputs and is asked to explicitly compare them: does the contextual evidence
explain the statistical trend, or is there a contradiction? The prompt asks for every claim in
the final answer to be traced back to whichever evidence path it actually came from (tagged, in
the rendered result, as coming from the statistical data vs. the contextual research), rather
than blending both sources into one unattributed narrative.

In live runs of this exact question, this reconciliation stage has surfaced a genuine, non-obvious
tension: the raw statistics show steadily improving performance right up through Leonard's next-
to-last game in the window, yet four days after his final game the Spurs announced he was shut
down indefinitely — a case where the numbers alone would predict the opposite of what actually
happened, and only the contextual evidence path explains why.

## How to run it

**Prerequisites:** Postgres running and migrated, the seed data loaded
(`scripts/seed-sports-demo-data.sql`), and `OrchestAI.API` running locally with a real
`Anthropic__ApiKey` (and, for live cited research rather than the model's own background
knowledge, a real `Tools__Perplexity__ApiKey`). Full setup detail is in
[docs/phase3-sports-demo-runbook.md](phase3-sports-demo-runbook.md).

**Option A — watch it live in the existing frontend** (recommended for an audience): start the
frontend (`cd frontend && VITE_API_URL=http://localhost:5000 npm run dev`), bootstrap a tenant +
API key (see the runbook's Step 0), paste the key into the frontend's prompt, then paste in the
question above (or reuse the exact wording from the runbook — the date range matters, see the
runbook's note on why). Submit, and watch the same generic agent-execution view every OrchestAI
task already uses: live per-agent cards, tool calls as they happen, and the final markdown
result rendered in place.

**Option B — run it from the command line:**

```bash
ADMIN_SECRET=<value matching Admin__BootstrapSecret> ./scripts/run-sports-demo.sh
```

This wraps the exact same steps as the runbook (bootstrap, submit, poll, print the result) —
it's a convenience script, not a different code path.

## What to pay attention to while watching it

- Two agent executions run concurrently (`Data`, `Research`), not one — visible as two separate
  cards/executions as they start together.
- `DataAgent`'s tool activity shows a real SQL query against `SportsPerformanceGames`, not a
  hallucinated number — the result reflects the actual seeded rows.
- `ResearchAgent`'s tool activity shows real, live `perplexity_search` calls, with cited results.
- The final result is a single rendered markdown report, produced by a *third*, later step
  (the reconciliation stage) that runs only after both agents above have finished — visible as a
  separate execution that starts later than the other two.
- The final report's claims are attributed back to whichever evidence path produced them, and it
  states plainly whether the two paths agreed or where the tension is — this attribution is the
  actual point of the demonstration, not the specific sports trivia.

## Scope

This is one bounded use case: one athlete, one performance question, two agents, the existing
seeded data and `db_query`/`perplexity_search` tools, and OrchestAI's existing, unmodified
orchestration/reconciliation engine and generic frontend. It does not cover other athletes,
other sports, or a general-purpose analytics surface — see
[docs/phase3-domain-notes.md](phase3-domain-notes.md) for the fuller record of what this sample
is and isn't, and why.
