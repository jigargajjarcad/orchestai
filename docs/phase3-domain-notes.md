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
