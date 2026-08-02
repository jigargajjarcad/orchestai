# Roadmap — OrchestAI

**Baseline:** `main` = `8fab095` (2026-08-02)
**Basis:** release retrospective + independent audit. No invented work — every item traces to
an observed gap.

---

## Governance — read before planning

Two approved decisions bound this roadmap. Neither is overridden here.

1. **ADR-014, Confirmation #11** — the frontend is internal/demo-only, revisited *"only if a
   future milestone explicitly calls for a public-facing demo (hiring conversation, real
   customer demo, actual end-user requirement)"*. The product-completion backlog already
   exercised this condition. **Still valid.**
2. **`docs/product-completion-backlog.md` §0** — *"Out of process entirely: Track B, external
   validation, market-validation exercises, scoring frameworks, commercialization strategy."*

**Consequence:** everything under *Long-term → Adoption* is a **proposal to reopen §0**, not a
plan. It needs an explicit decision first, framed the way ADR-014's own reopening was framed.

### Reconciliation with the existing backlog

`docs/product-completion-backlog.md` defines a 7-milestone sequence. Its status as of this
release:

| Backlog area | Status |
|---|---|
| A — Frontend / product experience | **Done.** Design tokens, component system, shell, routing, responsive, onboarding + API-key management, `remark-gfm`, loading/empty/error states, accessibility (far beyond the "baseline" it asked for) |
| B — Documentation website | **Not started.** 16 items |
| C — Developer experience | **Partial.** `frontend/README.md` still the Vite template |
| D — Packaging / release | **Not started.** No csproj package metadata |
| E — Deployment / demonstration | Config exists; live-instance freshness unverified (backlog open item E1) |
| F — Backend | Ongoing |

This roadmap continues that sequence rather than replacing it.

---

## Short-term

### v1.1 — "Consumable" · ~1 week

**Goal:** make the framework installable, and close the defect class that produced the release
blocker.

**Business value:** High. The project's stated differentiator is that .NET has no equivalent to
LangGraph. Right now nobody can install it, which makes that claim untestable by anyone else.

**Scope**
- Land `feature/design-system-hardening` (H-DEBT-3 — derived token contract, RED/GREEN verified, currently uncommitted)
- NuGet package metadata on all four projects + CI publish job gated on tags (H-DEBT-2)
- Replace `frontend/README.md` (H-DEBT-1)
- Remove the 14 fixed sleeps from `f1-1.spec.js` (M-DEBT-1)
- `CHANGELOG.md`, SemVer policy, `v1.0.0` tag

**Effort:** ~1 week · **Dependencies:** none · **Risk:** Low — nothing touches runtime
behaviour except test inputs.

**Success criteria:** `dotnet add package` works from a clean machine; the token contract fails
if any status-text token is placed on `surface0`; zero `waitForTimeout` under
`tests/playwright/a11y/`.

---

### v1.2 — "Legible" · ~2–3 weeks

**Goal:** a stranger can understand, extend and contribute without reading the source.

**Business value:** High if open-sourcing or interviewing. This is the milestone that converts
a strong repository into a usable project.

**Scope**
- Documentation site (backlog area B)
- Expand `CONTRIBUTING.md`; issue/PR templates (M-DEBT-2)
- Frontend modularization — extract five components from `App.jsx` (M-DEBT-3)
- Route-level code splitting (L-DEBT-1)
- Agent/tool authoring guide with a worked `IAgent` example
- Architecture diagram + screenshots + a 60–90s demo video
- Low debt sweep: L-DEBT-3, L-DEBT-4, L-DEBT-5, N-DEBT-1, N-DEBT-2, N-DEBT-3

**Effort:** 2–3 weeks · **Dependencies:** v1.1 (docs should document an installable package)
**Risk:** Medium — the frontend extraction touches the largest files; mitigated by 356 browser
test executions.

**Success criteria:** no file over 500 lines in `frontend/src`; initial JS bundle under 300 kB;
docs site live; someone outside the project ships a custom `IAgent` using only the docs.

---

## Long-term

### v2.0 — "Hardened" · ~2–3 months

**Goal:** structural changes too large for a point release.

**Business value:** Medium — internal quality, not user-visible. Justified by the frontend
being the one area whose rigor visibly trails the backend's.

**Scope**
- Incremental TypeScript migration (M-DEBT-4) — `allowJs`, then tokens → shared components → pages
- **Fixture state matrix** — `empty / loading / populated / selected / error / partial`
  enumerated once and driven across surfaces. *This is the structural fix for the failure mode
  that escaped three audits.*
- Backend test project split into unit/integration (L-DEBT-2)
- Durable `IApprovalGateway` — retires ADR-004's recorded in-memory limitation
- OpenTelemetry / Prometheus export of the existing `Budgeting` + `Observability` data

**Effort:** 2–3 months · **Dependencies:** v1.2 · **Risk:** Medium-High — TypeScript touches
everything; the fixture matrix will surface findings that then need triage.

**Success criteria:** shared components and tokens typed; the state matrix drives the
accessibility suite; ADR-004 superseded rather than merely accepted.

---

### Adoption — ⚠️ requires reopening backlog §0

**Goal:** the first real users. **Not scheduled** — this needs a decision first.

The retrospective identified *"it has never met a user"* as the project's single biggest
weakness. Every quality signal today is internal: tests that can be run, docs that can be read,
history that can be inspected. None answers *"does this survive contact with someone who didn't
build it?"*

Prioritising credibility over popularity:

| Stage | Goal | Signal |
|---|---|---|
| 1 | Installable (= v1.1) | Prerequisite; no adoption target |
| 2 | Evaluable (= v1.2) | A stranger goes from `dotnet add package` to a running custom agent without reading source |
| 3 | **Five design partners, not five hundred users** | Five people who used it and reported what broke |
| 4 | Fix what they hit, publicly | Credibility comes from the response, not the code |

**Explicitly not recommended:** Hacker News / Product Hunt launches before Stage 3. A framework
that gets attention before it gets feedback burns its one first impression. The backlog's §0
instinct — that market validation distracts from engineering — is right about *ordering*, even
if permanent exclusion is not.

---

## Deliberately not on this roadmap

| Not doing | Why |
|---|---|
| **Further accessibility work** | WCAG AA is verified across 16 populated states. Remaining findings are best-practice, not conformance. Continuing here repeats the misallocation that spent three milestones on one axis while `App.jsx` grew to 1,208 lines. |
| **New agents or tools** | Backlog §0 forbids adding capability "to appear more capable", and the gap is consumability, not breadth — six agents, four tools, three providers already. |
| **A second `ILlmProvider`** | **Already done.** Anthropic + OpenAI + AzureOpenAI resolve from DI via `LlmProviderFactory`. Listed here because an earlier draft recommended it in error. |
| **Auth redesign** | Backlog §0 closes the API-key model. |
| **Fixing the `srOnly` triplication** | Documented deliberate tradeoff. |

---

## Never change

Invariants that earned their place. Changing any of these should require an ADR.

1. **Interfaces in `Domain`.** The structural claim the framework rests on.
2. **Fast-forward-only merges.** One merge commit in 337 is why the history is readable.
3. **The offline stub backend.** Deterministic e2e with no live service, keys or cost — the reason Playwright can gate every commit.
4. **RED/GREEN verification.** It caught genuinely vacuous tests in this very release.
5. **Comments that cite measurements and record rejected alternatives.** A "tidy the comments" pass would be actively destructive.
6. **The Two-Palette and Two-Face rules.** They survived three accessibility milestones intact because fixes were made *within* them.
