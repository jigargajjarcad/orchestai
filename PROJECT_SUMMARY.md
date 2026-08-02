# OrchestAI — Project Summary

**Status:** Released · `main` = `8fab095` · 2026-08-02
**History:** 337 commits, 2026-06-25 → 2026-08-02

> Orientation document. It states *what exists and where*, and links out rather than
> restating. For the deep versions see [ARCHITECTURE.md](ARCHITECTURE.md),
> [DECISIONS.md](DECISIONS.md) (17 ADRs), [DESIGN.md](DESIGN.md),
> [OBSERVABILITY.md](OBSERVABILITY.md) and [README.md](README.md).

---

## What OrchestAI is

A production-grade multi-agent AI orchestration framework for .NET 8.

The premise: Python has LangGraph, AutoGen and CrewAI. .NET has no equivalent with
production concerns built in. OrchestAI is a CQRS-structured orchestration engine where an
Orchestrator decomposes a task, dispatches specialist agents (in parallel or sequentially),
aggregates their output through a manager-review pass, and streams the whole execution to a
browser over SSE — with cost accounting, tenancy, budgets, checkpointing, evals and tracing
present from the start rather than retrofitted.

It is **both** a library (consumed via the `Domain` interfaces) **and** a running reference
application (API + React console) that demonstrates the library against a real domain.

---

## Architecture

Four projects, dependencies pointing inward:

| Project | Files / LOC | Role |
|---|---|---|
| `OrchestAI.Domain` | 109 / 2,318 | Entities, enums, **all interfaces** — no outward dependencies |
| `OrchestAI.Application` | 105 / 3,050 | CQRS commands, queries, handlers, DTOs (MediatR) |
| `OrchestAI.Infrastructure` | 130 / 25,103 | Agents, tools, providers, EF Core, repositories, budgeting, tenancy, security, evals, observability |
| `OrchestAI.API` | 20 / 1,640 | ASP.NET Core controllers + SSE. HTTP only |

*Infrastructure LOC includes ~10k of generated EF migrations.*

**The load-bearing decision:** `IAgent`, `IMcpTool`, `ILlmProvider` and `ILlmProviderFactory`
live in `Domain`. A consumer can implement an agent or a tool without referencing
`Infrastructure`. This is what makes the framework claim structural rather than aspirational,
and it is the one thing that must never be traded away for convenience.

**Execution flow**

```
User task → API → CQRS command → OrchestratorAgent
                                      ├── ResearchAgent ┐
                                      ├── CodeAgent     ├─ parallel or sequential
                                      └── DataAgent     ┘
                                            ↓ IMcpTool calls
                                      Manager review (2nd Orchestrator turn, ADR-005)
                                            ↓
                                      Aggregate → SSE → React console
```

---

## Major features

**Agents (6)** — Orchestrator, Research, Code, Data, Writer, Browser — all via `AgentBase`.

**Tools (4 `IMcpTool`)** — `FileSystemTool`, `DatabaseTool`, `PerplexityTool`, `FirecrawlTool`,
resolved through `ToolRegistry`.

**LLM providers (3)** — `AnthropicProvider`, plus `OpenAIProvider` and `AzureOpenAIProvider`
sharing `OpenAiCompatibleProviderBase`. Resolved by `LlmProviderFactory` from DI on
`ProviderId`, so adding a provider is a registration, not a code change. *The abstraction is
demonstrated, not merely declared.*

**Production concerns already present** — `Budgeting` (per-task tool-call caps, cost ledger),
`Tenancy` (tenant-private evals and limits), `Security` (API-key auth, PII redaction),
`Caching`, `Eval` (suites, scorers, regression reports, post-hoc LLM-judge scoring),
`Observability` (OpenTelemetry-shaped trace/span model, ADR-011).

**Frontend console** — React 19 + Vite. Four surfaces: Playground (live SSE execution graph),
Memories, Observability (timeline / summary / cost dashboard / error rates / compare), Evals
(suites / run / results / post-hoc). ~10,044 LOC.

---

## Design principles

Full system in [DESIGN.md](DESIGN.md). The rules that actually constrain day-to-day work:

- **Two-Palette Rule** — Catppuccin tones identify *what* (agent identity); harder saturated
  status tones identify *state*. Never blended on the same concept.
- **Two-Face Rule** — IBM Plex Sans for UI chrome, JetBrains Mono for data (IDs, timestamps,
  durations, costs, code). No exceptions in either direction.
- **Token tiers over value mutation** — when a colour fails a requirement, add a tier; do not
  repaint a token that is also used as a fill or border. Applied twice (`textMuted`, and the
  `*-text` status tier).
- **Dense instrument, not a SaaS dashboard** — the target feel is `htop` or Grafana Tempo.

Token values live in `frontend/src/theme/tokens.js` and are mirrored, verified 29/29 with zero
mismatches, in `DESIGN.md` frontmatter and `.impeccable/design.json`.

---

## Accessibility status

**WCAG 2.1 AA — conformant on all machine-verifiable criteria**, independently verified at
release across 16 populated surface states.

| Verified | Evidence |
|---|---|
| Contrast (1.4.3) | axe-core clean on every surface in populated *and selected* states |
| Focus visible (2.4.7) | 48 tab stops across 4 views, every one with a visible ring |
| Focus order (2.4.3) | Focus survives run submission; dialogs return focus to trigger |
| Reflow (1.4.10) / Resize (1.4.4) | Zero horizontal overflow at 320px and 200% zoom |
| Info & relationships (1.3.1) | Section captions are real headings; no heading-level skips |
| Target size (2.5.8) | Nothing under 24×24 |
| Modals | Drawer and DetailPanel traps hold; Escape closes; scroll locked |

**Known non-conformances:** none at A/AA. The API-key gate lacks `<main>` and `<h1>` — axe
best-practice rules, not success criteria.

The full audit, including 18 Medium/Low findings left as recommendations, is in
[docs/f1-1-human-accessibility-report.md](docs/f1-1-human-accessibility-report.md).

---

## Testing status

**As released at `8fab095`:**

| Layer | Count | Notes |
|---|---|---|
| Backend | **408** methods (399 `[Fact]`, 9 `[Theory]`) across 105 files / 13,399 LOC | Single `OrchestAI.Tests` project |
| Frontend unit | **166** Vitest across 20 files | jsdom; structure, tokens, semantics |
| Frontend browser | **356** passed / 4 skipped | 120 Playwright definitions × 3 viewports (1280/768/375) = 360 executions |

**Why the split matters:** jsdom cannot evaluate composited contrast, real focus movement, or
layout. Those live in Playwright and only there. The 4 skips are a legitimate
`viewport.width >= 768` guard on mobile-drawer tests.

**Suite properties worth preserving:**
- **Offline stub backend** (`tests/playwright/fixtures/stub-api.mjs`) — deterministic e2e with
  no live service, no API keys, no cost. This is what lets the browser suite gate every commit.
- **Negative controls** — asserts the reconnect banner does *not* appear on a healthy stream.
- **A provable race test** — holds request A, renders B, releases A, asserts B survives, using
  task-id-derived values so identical fixtures cannot make it vacuous.
- **RED/GREEN enforced** — regression tests are verified to fail against reverted source before
  being accepted.

---

## CI/CD status

`.github/workflows/ci.yml` — **5 jobs**:

| Job | Catches |
|---|---|
| `build-and-test` | Backend compile + 408 tests |
| `migration-validation` | EF migrations that pass tests and fail on deploy |
| `container-smoke-test` | Image builds and actually boots |
| `security-scan` | Dependency/vulnerability scanning |
| `frontend-build-test` | Build, lint, Vitest, **and the full Playwright suite** |

Deployment: Railway (API + PostgreSQL 15), Vercel (frontend). Config in `Dockerfile`,
`docker-compose.yml`, `railway.json`. Operational procedures in [RUNBOOK.md](RUNBOOK.md).

Per ADR-016, `required_status_checks` cannot coexist with direct-push-to-main — a permanent
GitHub limitation. **Local gates before merge are therefore the real quality gate.**

---

## Release summary

`8fab095`, 2026-08-02 — 17 commits fast-forwarded from `09fc818`, zero merge commits.
Three stacked milestones: F2 (Verification Durability), F1 (Accessibility Conformance),
F1.1 (Human Accessibility Verification), plus one final blocker fix.

Full detail in [RELEASE_HISTORY.md](RELEASE_HISTORY.md).

---

## Where to look next

| Question | Document |
|---|---|
| What should I work on? | [ROADMAP.md](ROADMAP.md) |
| What's broken or owed? | [TECHNICAL_DEBT.md](TECHNICAL_DEBT.md) |
| Why is it built this way? | [DECISIONS.md](DECISIONS.md) — 17 ADRs |
| What did we learn? | [LESSONS_LEARNED.md](LESSONS_LEARNED.md) |
| How do I explain this to someone? | [PORTFOLIO.md](PORTFOLIO.md) |
