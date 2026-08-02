# OrchestAI — Engineering Portfolio Overview

**Multi-agent AI orchestration framework for .NET 8**
337 commits · 2026-06-25 → 2026-08-02 · `main` = `8fab095`

---

## The 30-second version

Python has LangGraph, AutoGen and CrewAI. .NET has no equivalent with production concerns built
in. OrchestAI is a CQRS-structured orchestration engine where an Orchestrator decomposes a
task, dispatches specialist agents in parallel, aggregates through a manager-review pass, and
streams the entire execution to a browser over SSE — with cost accounting, tenancy, budgets,
checkpointing, evals and distributed tracing present from the first week rather than bolted on.

It ships as both a library (consumed through dependency-free `Domain` interfaces) and a running
reference application with an observability console.

---

## Why this is technically interesting

### It is a distributed-systems problem wearing an AI costume

The genuinely hard parts have nothing to do with prompting:

- **Streaming a long-running, multi-actor process to a browser.** SSE cannot send an
  `Authorization` header, so authentication needed a short-lived single-use ticket minted by a
  normal authenticated call. That in turn means a dropped connection cannot be rescued by the
  browser's native retry — the credential is already spent — so the recovery path is explicit,
  announced, and user-driven.
- **Concurrency with real money attached.** Agents run in parallel against a paid API. That
  requires per-task tool-call budgets, a cost ledger, and admission control — implemented in
  `Infrastructure/Budgeting`, not deferred.
- **Request races that corrupt what the user sees.** Switching a selection left the previous
  request in flight, and whichever resolved *last* won — repainting the view with data the
  picker no longer named. Fixed, and covered by a test that holds request A, renders B,
  releases A, and asserts B survives, using task-id-derived values so identical fixtures cannot
  make the assertion vacuous.
- **Reconstructing execution hierarchy.** Span parent/child relationships are driven strictly
  by `spanId`/`parentSpanId` data, never inferred from timestamps or visual proximity — a rule
  written into ADR-011 and mirrored on both sides of the stack.

### The abstractions are demonstrated, not declared

`IAgent`, `IMcpTool` and `ILlmProvider` live in the dependency-free `Domain` project — a
consumer can implement an agent without referencing `Infrastructure`. `ILlmProvider` has
**three** implementations (Anthropic, OpenAI, Azure OpenAI) resolved from DI by `ProviderId`.
An interface with one implementation is a guess; with three it is a verified seam.

### It has an evaluation subsystem

Eval suites, scorers, regression reports against a baseline run, and post-hoc LLM-judge scoring
of historical traces — tenant-private, with a documented data model (ADR-012, ADR-013). This is
the part most AI projects skip entirely, and it is what separates "I called an LLM" from "I can
tell whether the change I just made was an improvement."

---

## What distinguishes it from a CRUD project

| Ordinary project | This project |
|---|---|
| Tests exist | Tests are verified to **fail** before being trusted — RED/GREEN enforced; it caught two of my own tests passing for the wrong reason |
| "Accessible" | WCAG 2.1 AA independently verified across 16 populated surface states, including *selected* states |
| README | 485-line README, 17 ADRs, `ARCHITECTURE.md`, `OBSERVABILITY.md`, `RUNBOOK.md` |
| CI runs tests | 5 CI jobs including **migration validation** and a **container smoke test** — the two that catch failures which only appear on deploy |
| Commits | 337 commits, 99% conventional, **1 merge commit**, 77% with substantive bodies |
| Happy path | Negative controls, race tests, a real dropped-SSE-connection test against an offline stub backend |

---

## The story worth telling in an interview

**A WCAG contrast failure escaped three consecutive accessibility audits, each more rigorous
than the last, and was caught one step before merge.**

| Pass | Scope | Why it missed |
|---|---|---|
| Milestone F1 | 11 surfaces, axe reported clean | Ran against **empty** states |
| Milestone F1.1 | Populated screens | Never **selected** an item |
| Independent pre-merge audit | Selected state | Caught it — 4.45:1 against a 4.5:1 requirement |

The audits improved every time. The *fixtures* never did. Rigor applied to the wrong input
space is wasted rigor.

The root cause was sharper still: the token test validated against a **hardcoded four-element
list** of surface tiers — how the palette is *organised*, not what the product *paints*. Three
real backgrounds were missing, including the one where the failure lived.

**The remediation is the interesting part.** Not a longer list — a list that cannot be
forgotten: a role map covering every colour token, guarded by an exhaustiveness assertion. Add
a token, and the build fails until someone classifies it; classify it as a background and every
text token is validated against it automatically. Demonstrated RED/GREEN four ways, including
proving the guard catches the *omission itself*, not merely its consequence.

**Why this is a senior signal:** most engineers can tell you about a bug they fixed. Fewer can
show a bug that escaped their own process three times, explain precisely why their process
allowed it, and then change the process so that class of defect cannot recur silently.

---

## Engineering practices demonstrated

- **Architecture Decision Records** — 17, including rejected alternatives and, critically, the
  *conditions under which each decision may be reopened*. ADR-014's reopening condition was
  later satisfied and exercised deliberately rather than quietly ignored.
- **Accepted risk named in writing** — *"Track B (external human validation) deliberately
  deferred, not completed — accepted risk."* A deferral that is written down is a decision.
- **Release discipline** — fast-forward-only merge, ancestry verified against a freshly fetched
  ref, all gates re-run on the merged result before push.
- **Escalating a blocker** — the release was stopped one step from merge over a 0.05 shortfall
  in a contrast ratio, because the release's headline claim was AA conformance and shipping a
  known counterexample under that banner is a documentation lie.
- **Proven vs inferred, stated explicitly** — which is what stopped an overclaim about graph
  edges leaving the accessibility tree when three unnamed SVG wrappers remained.

---

## Scale

| | |
|---|---|
| Backend | 364 files, ~32k LOC (Infrastructure includes ~10k generated EF migrations) |
| Backend tests | **408** methods, 105 files, 13,399 LOC |
| Frontend | 10,044 LOC (non-test) |
| Frontend tests | 166 Vitest + 360 Playwright executions (120 definitions × 3 viewports) |
| Documentation | 17 root documents (~5.8k lines) incl. 17 ADRs, plus 25 planning docs |
| CI | 5 jobs |

---

## Honest limitations

Stated because a reviewer will find them, and because naming them is the point.

- **It has never met a user.** No external adopters, no production traffic. Every quality signal
  is internal. This is the project's single biggest weakness and it is recorded as accepted risk
  in the repository itself.
- **Not yet installable.** No NuGet package metadata, so the "framework" cannot currently be
  consumed via `dotnet add package`. First item on the roadmap.
- **Frontend rigor trails the backend.** `App.jsx` is 1,208 lines containing five components,
  and there is no TypeScript on 10k lines of JSX. Three milestones went into accessibility while
  this sat unaddressed — a real judgment gap about *when to stop polishing one axis*.
- **Effort was disproportionate on one dimension.** The accessibility work is above what most
  commercial products reach; that is defensible as a demonstration of rigor and questionable as
  resource allocation.

Full detail: [TECHNICAL_DEBT.md](TECHNICAL_DEBT.md) · [ROADMAP.md](ROADMAP.md)

---

## How this was built

AI-native workflow: the code is written by Claude Code under direction, review and architectural
control. The artifacts that demonstrate that control are the ones worth examining — the ADRs
with their reopening conditions, the deferrals named as accepted risk, the blocker escalated
rather than waved through, and the process change made after the same defect class escaped three
times. Velocity (337 commits in 5.5 weeks) is the least interesting thing about it.

---

## Where to look first

| If you have | Read |
|---|---|
| 2 minutes | [README.md](README.md) — what it does, API reference, agent capability matrix |
| 10 minutes | [PROJECT_SUMMARY.md](PROJECT_SUMMARY.md), then [ARCHITECTURE.md](ARCHITECTURE.md) |
| An hour | [DECISIONS.md](DECISIONS.md) — 17 ADRs; the real engineering record |
| An interest in process | [LESSONS_LEARNED.md](LESSONS_LEARNED.md) and [docs/f1-1-human-accessibility-report.md](docs/f1-1-human-accessibility-report.md) |
