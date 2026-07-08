# Product

## Register

product

## Users

.NET/backend engineers operating OrchestAI in production, or evaluating it as a portfolio
piece — debugging a specific multi-agent run, tracking spend across agents/models, or
comparing two runs after a prompt or model change. The primary task on any given screen is
answering "what happened, why, how much did it cost, how do I reproduce it" without touching
Postgres or reading raw logs.

## Product Purpose

OrchestAI is a production-grade C# .NET 8 boilerplate for multi-agent AI orchestration
(CQRS + MediatR), and this frontend is its observability dashboard — the LangSmith-equivalent
for the .NET ecosystem. It exists because there's currently nowhere in .NET to see a
multi-agent run's execution timeline, cost breakdown, error rate, or a side-by-side prompt
comparison without querying the database directly. Success looks like: an engineer opens one
screen and gets a complete answer, live or historical, in seconds.

## Brand Personality

Precise, technical, trustworthy. An engineer's instrument panel, not a consumer product —
dense, information-forward, built by engineers for engineers. References: LangSmith (the
trace/span visualization pattern — nested Gantt spans, per-run cost/token breakdown) and
Grafana Tempo (the dense, dark, terminal-native aesthetic of a tracing tool that doesn't
apologize for showing real data).

## Anti-references

Generic SaaS dashboard: no gradient hero metrics, no cream/sand card grids, no rounded pastel
"consumer analytics" look. This is not a marketing-adjacent product surface — it should never
read as a Stripe-dashboard-clone or a landing-page-shaped admin panel.

## Design Principles

- **Data density over decoration.** An engineer debugging a failed run wants information, not
  whitespace for its own sake — every pixel should earn its place by answering a real question.
- **Trace/span IDs are the source of truth, not timestamps.** The UI must always reconstruct
  hierarchy (nested spans, parent/child execution) from explicit IDs, never from inferred
  ordering — this mirrors the backend's own architectural decision (ADR-011) and should be
  visible in how the timeline is built and rendered.
- **Live and historical data must look the same.** Whether a run just finished or happened
  three weeks ago, the same screen answers the same question the same way — no separate "live
  view" vs. "history view" mental model.
- **Numbers are never decorative.** Cost, tokens, duration, and error rates are real production
  data an engineer will act on (debug, optimize, budget) — precision and correctness in what's
  displayed matter more than visual polish.
- **Show the failure, don't hide it.** Error states, retries, and failed spans are first-class
  citizens in the timeline and error-rate views, not an afterthought buried in a log line.

## Accessibility & Inclusion

WCAG AA baseline: body text ≥4.5:1 contrast, fully keyboard-navigable. Reduced-motion support
required on any future animation work (`prefers-reduced-motion`). No additional accommodation
requirements known at this stage — revisit if real users with specific needs are onboarded.
