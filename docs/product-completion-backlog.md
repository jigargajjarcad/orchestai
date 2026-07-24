# OrchestAI — Product Completion Backlog

**Status:** Durable planning reference. Not a task-by-task implementation plan.
**Purpose:** Single source of truth for what remains to turn the validated OrchestAI
engineering foundation into a genuinely polished, commercially credible product experience.
Reviewed and approved here before any implementation milestone begins.

This is a backlog and sequencing reference, not a locked plan — individual milestones get
their own planning treatment (following this project's existing `docs/superpowers/plans/`
convention) only when a milestone is actually started.

---

## 0. Locked context — do not reopen

The following are treated as final decisions for the duration of this backlog, per direct
instruction. Nothing below revisits them.

- **Product-completion direction is decided and final.** OrchestAI is being developed into a
  polished, commercially credible product around its existing engineering foundation.
- **Auth model is decided and closed:** the existing API-key model stays. No
  backend-for-frontend/session auth, no httpOnly cookies, no enterprise identity, no
  self-service signup. Work in this scope only makes the *existing* model feel intentional,
  clear, and documented — it does not expand authentication surface. The one exception: a
  concrete security or functional defect in the current model, if one is found, may be fixed
  without that being scope expansion.
- **Out of process entirely:** Track B, external validation, market-validation exercises,
  scoring frameworks, commercialization strategy. Not mentioned again below except to note
  their exclusion.
- **No new orchestration-engine capability.** No new agent types or tools added to appear more
  capable.
- **No new formal phase name.** This document is referred to as the product-completion
  backlog, not "Phase 4" or any other phase label — none is defined in this repository and none
  is introduced here.
- **The core engineering foundation (Weeks 1–12, Phase 1–3, Workstreams 1–2) is complete** and
  is not re-evaluated here. No concrete blocker was discovered while building this backlog.
- **This reopening of frontend/product investment is itself the exercise of ADR-014's own
  stated reopening condition** (`DECISIONS.md:701-702`: "revisit only if a future milestone
  explicitly calls for a public-facing demonstration") — it does not override or reinterpret
  ADR-014, it satisfies the condition ADR-014 itself named.

---

## 1. Evidence base and re-confirmation

The primary evidentiary base is the prior product-completion investigation (this
conversation). Facts that could plausibly have drifted were re-confirmed against the live
repository before this document was written; no repository changes had occurred in the
interim (`git log` shows the same commit, `c3e6cef`, as the investigation's end state).

Re-confirmed this session:
- `frontend/package.json` — no Tailwind, no `remark-gfm`, no TanStack/React Query dependency
  (`frontend/package.json:11-14`).
- `.impeccable/design.json:36` — `"breakpoints": []`, still empty.
- `apiKey.js` exports `getApiKey()`/`clearApiKey()` (`apiKey.js:28,36`), but neither is
  imported or called anywhere in `App.jsx` or any other frontend file — **new finding this
  session**: there is no UI affordance to see which key is active or to clear/change it.
- `src/OrchestAI.Domain/OrchestAI.Domain.csproj`, `OrchestAI.Application.csproj`,
  `OrchestAI.Infrastructure.csproj` — none carry `PackageId`, `Description`, `Authors`, or
  `RepositoryUrl` metadata (grep returned zero matches) — **new finding this session**,
  relevant to §D below.
- `scripts/pack-local-nuget.sh:7` — the only version identifier anywhere in the repo is a
  hardcoded local variable, `VERSION="0.1.0-phase2"`, embedding an internal phase name into
  what would be a public package version string. No `CHANGELOG.md` exists anywhere in the repo
  (confirmed via `find`). No git tags exist (`git tag -l` returned empty).
- `frontend/README.md:1-11` — still the unmodified Vite/Oxlint scaffold default.

Facts carried forward unchanged from the prior investigation (not re-derived in full here):
frontend structure and line counts (`App.jsx` 804, `ObservabilityPage.jsx` 601,
`EvalsPage.jsx` 475), `DESIGN.md`/`.impeccable/design.json` alignment with actual implemented
colors, absence of any documentation-site plan anywhere in git history, and the existing
Railway/Vercel deployment documentation (`README.md:400-448`).

Labeling convention used throughout this document: **[Fact]** = directly supported by
repository content; **[Assumption]** = plausible but not verifiable from the repo alone;
**[Judgment]** = this document's own recommendation, not a repository fact.

---

## 2. Backlog by Area

Classification key: **MUST** (required for a genuinely complete, polished product) · **DEFER**
(valuable, product is honestly complete without it) · **OUT** (excluded by the locked
decisions in §0).

### A. Frontend / Product Experience

| # | Item | Class | Why |
|---|---|---|---|
| A1 | Design token extraction from `DESIGN.md`/`.impeccable/design.json` into a real, shared token file | **MUST** | Foundational — every other frontend item depends on this existing first. **[Fact]** the tokens are already fully specified (`DESIGN.md:100-269`, `.impeccable/design.json:6-79`) and already match production usage; this is extraction, not design work. |
| A2 | Reusable component system (Button, Panel/Card, StatusBadge, Input, Nav item) | **MUST** | **[Fact]** every one of these is currently a hand-written inline `style={{}}` object repeated across `App.jsx`, `ObservabilityPage.jsx`, `EvalsPage.jsx` with no shared source. Blocks A3–A6, A22–A27. |
| A3 | Application shell (header/nav rebuilt on the component system) | **MUST** | Depends on A1/A2. Current nav is inline styles at `App.jsx:610-651`. **[New finding, Milestone 1]** the documented `'JetBrains Mono', 'Fira Code', monospace` font-stack (`DESIGN.md:157-158`) is not loaded as a webfont anywhere (`frontend/index.html` has no font `<link>`, `frontend/public/` contains only `favicon.svg`/`icons.svg`) — a user without either font installed silently sees a generic system monospace fallback instead of the documented identity. Tracked here for later review; no action taken on it in Milestone 1 (see A7 cross-reference and the Milestone 1 plan, `docs/superpowers/plans/2026-07-23-milestone1-frontend-foundation.md` §8 decision 5, for the full record). **[Two further findings, Milestone 1 Task 1]** `index.css`'s legacy Vite-template `h1`/`h2`/`code` element selectors turned out not to be dead code: (1) they leak a sans-serif font-family and negative letter-spacing onto `App.jsx`'s page-title `<h1>` and `ApiKeyPrompt.jsx`'s `<h2>` (neither sets those two properties inline), conflicting with `DESIGN.md`'s mono-only rule; (2) they render `EvalsPage.jsx:71`'s inline `<code>` snippet as a light-cream chip with near-black text against the dark Catppuccin panel. Both explicitly deferred to Milestone 2/3, not fixed in Milestone 1 — see the Milestone 1 plan §8 for the full record and evidence. **[Third finding, Milestone 1 Task 2, corrected]** `AgentCard` and `ManagerReviewCard` write `${statusColor}40` — a hex alpha suffix (~25.1% decimal), not the 40%-as-percentage `DESIGN.md` documents for this border; same class of hex-vs-percentage transcription bug as the badge alpha, not pre-approved for correction, so ~25.1% is preserved exactly in Milestone 1's new `Panel` component. `ApprovalCard` does **not** share this pattern — it hardcodes a different literal, `#f59e0b60` (~37.6% decimal) — an earlier version of this finding wrongly generalized all three components together; `ApprovalCard`'s border is left as its own local, untouched one-off rather than migrated onto `Panel` at all. Both deferred to Milestone 2/3 alongside the other findings above. |
| A4 | Client-side routing (URL-addressable views) | **MUST** | **[Fact]** view switching is `useState('playground')` (`App.jsx:351`), not URL-based — no deep links, no bookmarking, no back-button support. This reads as a toy SPA, not a product, at the "commercially credible" bar. |
| A5 | Responsive design — down to common laptop/tablet widths | **MUST** | **[Fact]** zero media queries in any real page component; `.impeccable/design.json:36` records `breakpoints: []`. A product that only renders correctly at one fixed width is not credible. |
| A5a | True mobile/phone optimization of dense views (timeline Gantt, cost tables) | **DEFER** | **[Judgment]** the audience (engineers debugging a run at a desk) makes phone-width the low-value edge case; err toward defer per your instruction to lean that way on debatable items. |
| A6 | Navigation & information architecture consistency | **MUST** | Same work as A3/A4, listed separately only because your brief calls it out explicitly. |
| A7 | First-run / product-entry experience, within the closed API-key model | **MUST** | **[Fact]** `ApiKeyPrompt.jsx` (45 lines) is the only place in the entire frontend that breaks the mono-only visual rule (`fontFamily: 'sans-serif'`, line 14) and carries zero product framing — plain prose, no context on what OrchestAI is or what the key is for. **Scope explicitly covers the whole first-run path, not just the key prompt:** the pre-key screen must establish OrchestAI's identity and what the key is for, and the initial post-key landing state must give a coherent "what do I do here?" answer — a clear primary action (run the first workflow) plus orientation to the Playground, Memories, Observability, and Evaluations areas, with documentation links where appropriate once §B's docs site exists. This is a first-run *orientation* requirement, not a marketing landing page — no dedicated in-app landing screen is required beyond what the existing four-area nav (`App.jsx:610-651`) already provides once labeled/oriented properly. The Playground's default empty-result state is the same first-run path and is addressed jointly with A23 below, not as separate new scope. See also A3's webfont-fidelity finding — OrchestAI's identity as presented here relies on the same not-currently-loaded font stack. |
| A8 | API-key presentation/management within the existing model | **MUST** | **[New finding]** `getApiKey()`/`clearApiKey()` exist (`apiKey.js:28,36`) but are never wired to any UI — there is no way to see which key is active or to change/clear it without clearing browser state manually. Fixing this is within the closed auth model (no new auth mechanism), just a missing UI affordance for the existing one. |
| A9 | Agent execution experience (submission form, live SSE feed) | **MUST**, restyle only | **[Fact]** functionally complete (`App.jsx:335-804`); needs componentizing onto A1/A2, not rebuilding. |
| A10 | Agent cards | **MUST**, restyle only | `AgentCard` (`App.jsx:55-127`) is functionally complete. |
| A11 | Tool-call visualization | **MUST**, restyle only | `ToolCallRow` (`App.jsx:20-53`) functionally complete, including truncation at 120 chars. |
| A11a | Expandable/full tool-call output (beyond the 120-char truncation) | **DEFER** | **[Judgment]** existing truncation is a reasonable, deliberate design choice, not a defect. |
| A12 | Human-in-the-loop approval experience | **MUST**, restyle only | `ApprovalCard` (`App.jsx:129-218`) already has a considered approve/reject-with-note flow. **[Deferred finding, Milestone 1]** its Approve (green fill) / Reject (red fill/ghost) buttons conflict with `DESIGN.md`'s documented "One Accent Rule" (Trace Blue is meant to be the only color used for interactive affordances) — this is a deferred design decision, not a Milestone 1 defect that was silently resolved; Milestone 1 intentionally made no visual change to these buttons. A future milestone must decide whether to preserve the current green/red semantic differentiation as an accepted exception, or redesign the two actions to comply with the One Accent Rule (e.g. via icons, label wording, or another non-color-based distinction) — the current green/red treatment is not to be treated as the finalized design-system standard. See the Milestone 1 plan, `docs/superpowers/plans/2026-07-23-milestone1-frontend-foundation.md` §8, for the full record and evidence. |
| A13 | Manager/reconciliation synthesis experience | **MUST**, restyle only | `ManagerReviewCard` (`App.jsx:220-253`) is minimal but functional; no structural rebuild needed. |
| A14 | Final result presentation | **MUST**, restyle only | `ReactMarkdown` rendering with custom component overrides already exists (`App.jsx:776-791`). |
| A15 | `remark-gfm` fix | **MUST** | **[Fact]** confirmed still absent from `package.json`; known, previously tracked, small effort. |
| A16 | Memories experience | **MUST**, restyle only | `MemoriesPage` (`App.jsx:255-333`) functionally complete for its scope (list/delete). |
| A16a | Memories filtering/search/pagination | **DEFER** | **[Judgment]** not flagged as broken; current table view is adequate at expected scale. |
| A17 | Observability dashboard (5 sub-views) | **MUST**, restyle only | All five (`ObservabilityPage.jsx`) are wired to real endpoints and functionally complete. |
| A18 | Timeline/trace visualization | **MUST**, restyle only | Custom Gantt (`ObservabilityPage.jsx:129-240`) already respects the span-hierarchy architectural rule (ADR-011). |
| A19 | Cost/usage views | **MUST**, restyle only | `DashboardView`/`BarChart` functionally complete. |
| A20 | Error views | **MUST**, restyle only | `ErrorRatesView` functionally complete. |
| A21 | Evaluation UI — suites/runs/results/regression/post-hoc | **MUST**, restyle only | All four sub-views in `EvalsPage.jsx` (475 lines) are genuinely feature-complete against real endpoints; no structural gaps found. |
| A22 | Loading states | **MUST** | **[Fact]** currently plain "Loading…" text, reimplemented independently per page; a real product needs one consistent treatment, sourced from A2. |
| A23 | Empty states | **MUST** | **[Fact]** some empty-state copy is raw developer-facing API hinting (e.g. `EvalsPage.jsx:70-72`: "create one via `POST /api/v1/eval-suites`") — not appropriate copy for a polished product. This includes the Playground's own default empty-result panel (`App.jsx:794-797`, "Submit a task to see results here.") — the first meaningful screen most users see after first-run — which is covered here jointly with A7's first-run/product-entry scope, not as separate new work. |
| A24 | Error states | **MUST**, componentize only | Already reasonably consistent (red banners); fold into A2. |
| A25 | Accessibility baseline — ARIA labels, keyboard operability, visible focus states | **MUST** | **[Fact]** no ARIA labels on icon-only nav buttons (🧠/📊/🎯, `App.jsx:621-650`); `DESIGN.md:224-226` itself documents no custom focus treatment exists. Cheap relative to impact; directly part of the credibility bar. |
| A25a | Full formal WCAG AA audit/certification | **DEFER** | **[Judgment]** `PRODUCT.md:55-59`'s WCAG claim was always aspirational, not a compliance obligation for this goal; baseline accessibility (A25) is the credible bar, formal audit/certification is not required to honestly call this complete. |
| A26 | Final cross-page UX consistency pass | **MUST** | Last item in A — verifies A1–A25 actually cohere once done, not a separate design effort. |

### B. Documentation Website

| # | Item | Class | Why |
|---|---|---|---|
| B1 | Framework/tooling selection | **MUST** | **[Fact]** no prior tooling decision exists anywhere (confirmed absent in the prior investigation and not contradicted since). |
| B2 | Information architecture + navigation | **MUST** | Needed to organize the (already strong) existing content into a real site. |
| B3 | Search | **DEFER** | **[Judgment]** at this project's current content volume, static-site default/browser search is adequate; dedicated search infrastructure is premature. |
| B4 | Introduction / What is OrchestAI / Why OrchestAI | **MUST**, adapt existing | Source material already exists in `README.md`'s opening, `PRD.md §19` (Product Strategy), `PRODUCT.md`. |
| B5 | Core concepts | **MUST**, adapt existing | Source: `ARCHITECTURE.md §§1-4`. |
| B6 | Getting Started / Installation / Configuration / Local development | **MUST**, adapt existing | Source: `README.md`'s Quick Start, `docs/packaging/README.md`. |
| B7 | First agent / first tool / first workflow | **MUST**, new writing | **[Fact]** confirmed absent in the prior investigation (targeted grep, zero hits) — genuine net-new content. |
| B8 | Multi-agent orchestration, MCP, Memory, Human-in-the-loop | **MUST**, adapt existing | Source: `ARCHITECTURE.md §§2,5`, `PRD.md §§9-10`. |
| B9 | Observability, Evaluations | **MUST**, adapt existing | Source: `OBSERVABILITY.md`, ADR-012/013. |
| B10 | Architecture, Multi-tenancy, Security, Cost controls, Abuse protection | **MUST**, adapt existing | Source: `ARCHITECTURE.md`, ADR-014/015. |
| B11 | Extension points (build a custom `IAgent`/`IMcpTool`) | **MUST**, new writing | **[Fact]** confirmed absent; pairs with C's extension-guidance item — same content, written once. |
| B12 | API/reference documentation | **MUST**, restructure existing | README's API section exists but is a flat list, not a real reference; Swagger/Swashbuckle already runs live (`README.md:144`) and should be the anchor, not a rebuild. |
| B13 | Deployment | **MUST**, adapt existing | Source: `README.md:414-448`, already accurate and complete. |
| B14 | Troubleshooting | **MUST**, consolidate existing | Source: `CONTRIBUTING.md`'s "Gotchas" section, `RUNBOOK.md` — needs consolidating into one page, not new investigation. |
| B15 | Contributing | **MUST**, adapt existing | Source: `CONTRIBUTING.md`, already good. |
| B16 | Sample applications / Sports walkthrough | **MUST**, adapt existing | Source: `docs/phase3-sports-sample-app.md`, `docs/phase3-sports-demo-runbook.md` — both already high quality; this is surfacing/linking work, not rewriting. |
| B17 | Versioned/multi-version documentation | **OUT** | No versioned releases exist yet (see §D) — premature until that changes. |

### C. Developer Experience

| # | Item | Class | Why |
|---|---|---|---|
| C1 | Root `README.md` improvements | **MUST**, narrow scope | Already strong post-cleanup; scope is limited to cross-linking the new docs site once it exists, not a rewrite. |
| C2 | Replace `frontend/README.md` | **MUST** | **[Fact]** confirmed still the unmodified Vite scaffold default; cheap, real gap, independent of everything else — can be done anytime. |
| C3 | Clear first-10-minutes journey | **MUST** | Ties directly to A7 (onboarding) and B6 (getting started); this is the wiring/sequencing of those, not separate content. |
| C4 | Extension guidance + example code | **MUST** | Same underlying content as B11; the example-code half specifically is currently zero (grep confirmed no extension tutorial/example exists anywhere). |
| C5 | API discoverability | **MUST**, narrow scope | **[Fact]** Swagger UI already exists and is documented (`README.md:144`) — this item is about surfacing it prominently in the new docs site/onboarding path, not building it. |
| C6 | Explicit map of repository ↔ package ↔ frontend ↔ sample app ↔ docs site relationships | **MUST** | **[Fact]** currently only implicit, pieced together from README plus PRD's disambiguation note (`PRD.md:1284-1293`) — cheap to write explicitly once, high value for a first-time reader. |

### D. Packaging / Release Experience

The packaging boundary itself (ADR-017) is not re-evaluated — confirmed complete.

| # | Item | Class | Why |
|---|---|---|---|
| D1 | NuGet package metadata (`PackageId`, `Description`, `Authors`, `RepositoryUrl`, license) on the three packaged `.csproj` files | **MUST** | **[New finding]** confirmed via grep: zero such metadata currently exists on `OrchestAI.Domain/Application/Infrastructure.csproj`. A package that `dotnet pack`s with no description or repo link is not presentable, even locally. |
| D2 | Real initial version + documented versioning convention, replacing the placeholder | **MUST** | **[Fact]** `scripts/pack-local-nuget.sh:7` hardcodes `VERSION="0.1.0-phase2"` — an internal phase name baked into a would-be public version string. **[Judgment — not decided here]** the exact initial version is deliberately *not* fixed by this backlog document; `0.1.0` in the prior draft was illustrative only, not a decision. The packaging milestone must choose the actual initial version at that time, based on the repository's real release maturity and evidence as it stands then (e.g. whether it should read as an early pre-1.0 or something else), and must document the versioning convention being adopted (even a one-line SemVer/pre-1.0-no-guarantee note is sufficient). The one fixed requirement: the internal `phase2` naming is removed from any public-facing package version string — that part is not a judgment call, it's a confirmed defect in the current placeholder. |
| D3 | `CHANGELOG.md`: retrospective content + a documented update convention | **MUST**, minimal scope | **[Fact]** confirmed absent anywhere in the repo. This item is exactly two things: (1) create a retrospective `CHANGELOG.md` covering the major already-completed engineering milestones (Weeks 1–12/Phase 1–3/Workstreams 1–2), and (2) write down, once, the convention that future releases or meaningful changes update it (e.g. a short "Keep a Changelog"-style note). Establishing that convention is a one-time piece of this same document, not a separate backlog item — actually *following* the convention going forward is a working habit, not additional implementation work, and carries no separate effort estimate. |
| D4 | Publishing to a public NuGet feed (nuget.org) | **DEFER** | **[Judgment]** ADR-017's validation was explicitly local-feed/spike scope; public publishing is a separate ownership/API-stability commitment, not required to honestly call the product complete. |
| D5 | Polishing `spikes/phase2-console-consumer/` into a public starter template | **DEFER** | Already demonstrates the capability technically; turning it into a polished public template is nice-to-have. |

### E. Deployment / Demonstration Experience

| # | Item | Class | Why |
|---|---|---|---|
| E1 | Verify/refresh the existing hosted demo instance | **MUST**, verification not construction | **[Ambiguity]** `App.jsx:9`'s fallback API base is `https://orchestai-production.up.railway.app` — this implies a real deployed instance exists, but its current liveness/freshness was not (and should not be, in a planning-only task) tested here. Flag for verification at execution time, not resolved in this document. |
| E2 | Coherent end-to-end narrative: discover → docs → install → first workflow → run → explore frontend/agents/observability/evals → architecture → extend | **MUST**, mostly cross-linking | This is substantially a consequence of A, B, and C being done and linked together, not new infrastructure — **[Fact]** all the individual pieces (Railway/Vercel deployment docs, CI/CD, sample app) already exist per `README.md:400-448` and prior investigation. |
| E3 | Public, no-key-required read-only demo mode | **DEFER** | **[Judgment]** this would be a genuinely new feature (public anonymous access path), not currently decided, and borders on "adding scope because a competitor has it." **Explicitly not required to declare this product-completion backlog complete.** It may be considered later, if at all, as a separate product decision, weighed against the additional infrastructure and security considerations it would introduce — it is not implicit scope for the current effort and must not be treated as such by any later milestone. |
| E4 | Additional cloud infrastructure beyond current Railway/Vercel | **OUT** | Directly excluded by your instruction: "do not add deployment infrastructure merely because a commercial competitor might have it." |

### F. Backend

The backend is fundamentally complete; this section stays intentionally small.

| # | Item | Class | Why |
|---|---|---|---|
| F1 | (see D1) NuGet metadata is a `.csproj` change but tracked once, under D, to avoid double-counting | — | — |
| F2 | Bundling existing seed/bootstrap scripts (`DatabaseSeeder`, `bootstrap-local-dev.sh`) into a single "zero to running" convenience path | **DEFER** | **[Judgment]** the components already exist independently and work; bundling them is a documentation/DX sequencing task (covered by C3), not new backend code. |
| F3 | A public read-only "demo mode" endpoint | **DEFER** | Only relevant if E3 is later approved; not built speculatively. |
| — | Any other backend change | **OUT**, unless a concrete blocker surfaces | No genuine product-completion dependency on new backend work was identified during this planning pass. If one surfaces during A/B/C execution, it gets added here with its own evidence — not assumed in advance. |

---

## 3. Dependency Map and Parallelization

```
A1 (tokens) ──▶ A2 (components) ──▶ A3/A4/A6 (shell, routing, nav)
                                  ├─▶ A5 (responsive)
                                  ├─▶ A7/A8 (onboarding, key mgmt)
                                  ├─▶ A9–A21 (restyle existing pages — independent of each other)
                                  ├─▶ A22–A25 (loading/empty/error/accessibility)
                                  └─▶ A26 (final consistency pass — last in A)

A15 (remark-gfm)  — independent, can land anytime inside A

B1 (tooling) ──▶ B2 (IA) ──▶ B4–B16 (content adaptation/writing — independent of each other,
                             except B7/B11 which are genuinely new writing and take longer)

C2 (frontend/README.md), C6 (relationship map)  — fully independent, can happen anytime
C1, C3, C4, C5                                  — depend on both A (onboarding/API pages
                                                   existing) and B (docs site existing) far
                                                   enough along to link to

D1–D3           — fully independent of A/B/C, can run anytime

E1 (verify demo) — independent, can run anytime
E2 (narrative)   — depends on A, B, and C all being substantially done

F                — no items currently block anything; F1 folded into D1
```

**Confirming the prior investigation's suggestion:** yes, **documentation-site foundation
work (B1–B3) can genuinely start in parallel with frontend modernization (A)** — they are
different codebases with no code dependency between them. The one soft link is that the docs
site's visual styling benefits from A1's extracted token file existing first, so the docs site
doesn't have to hand-derive the same values from `DESIGN.md` independently — but that's a
sequencing nicety, not a hard blocker; the docs site can start with `DESIGN.md`/
`.impeccable/design.json` directly and adopt the real token file later if convenient.

**Effort-time vs. calendar-time — the actual distinction for a solo developer:** because there
is exactly one developer, "parallel" tracks do not literally run simultaneously the way they
would on a team — the same person's finite evenings/weekends hours are the bottleneck either
way. What parallelization actually buys here is **avoiding forced idle/serial ordering**
(e.g., not sitting on the docs site until every single frontend item is finished), which
compresses the *calendar* span somewhat by letting D's quick wins and B's early setup absorb
lower-energy sessions alongside A's harder refactor work — but it does not multiply total
throughput the way a real second contributor would. Treat the calendar-time figure below as
modestly, not dramatically, shorter than a naive sum of every category's effort-time.

---

## 4. Effort Estimates

Same basis as the prior investigation: solo developer, evenings/weekends, full-time job
continuing in parallel. This project's own history (Weeks 10–12, Phase 1, Phase 3) has
consistently run past initial "quick" estimates once real work started — these numbers assume
that pattern continues, not that this backlog is somehow different.

| Category | Estimate | Main drivers |
|---|---|---|
| **A. Frontend/Product Experience** | **4.5–6.5 weeks** | Dominant cost. 2,105 lines of inline-styled JSX with zero existing component boundaries (confirmed fact) means every page gets touched; routing and responsive design are structural, not cosmetic; onboarding + API-key-management UI (A7/A8, the latter a new-this-session finding) adds real, if small, additional scope beyond the prior estimate. |
| **B. Documentation Website** | **1.5–2.5 weeks** | Confirmed no prior tooling exists (genuine setup cost), but the bulk of content (B4–B6, B8–B10, B12–B16) is adaptation of already-strong existing material — B7/B11 (new writing) are the real cost drivers within this category. |
| **C. Developer Experience** | **0.5–1 week** | Mostly small, several items overlap with B7/B11's writing (no double-counting) or are cheap/independent (C2, C6). |
| **D. Packaging/Release Experience** | **2–4 days** | All three items (metadata, version, changelog) are small, well-scoped, and independent — **[new-finding-driven]**, not previously estimated as a distinct line. |
| **E. Deployment/Demonstration Experience** | **0.5–1 week** | Mostly cross-linking once A/B/C exist; E1's verification step is cheap but must happen live, not assumed. |
| **F. Backend dependencies** | **~0–2 days** | No Must-Build backend item currently identified beyond D1 (counted once, under D). Reserve a couple of days only in case a concrete blocker surfaces during A/B/C execution. |
| **Final Integration/QA** | **1–1.5 weeks** | This project's own history (Phase 1, Phase 3) shows live end-to-end verification reliably finds real bugs that review alone misses — budget a genuine week, not a token pass. |

**Total effort-time: ~9–13 weeks.** Stated honestly, not compressed — this is a large number,
and it is larger than the prior investigation's 8–11 week figure specifically because this
backlog surfaced two new, real, small-but-genuine gaps (API-key management UI, packaging
metadata/versioning/changelog) that the higher-level investigation hadn't itemized.

**Realistic calendar-duration: ~3–5 months**, for a solo developer working evenings/weekends
alongside a full-time job. This is a calendar-duration framing, not a second, smaller effort
figure — the total human effort required stays the ~9–13 weeks of focused part-time
development effort stated above. Parallelization (§3: B/D/C2/C6/E1 genuinely not needing to
wait for A) reduces *dependency waiting and idle time* — it prevents the whole backlog from
sitting fully serial, one category strictly after another — but it does not materially reduce
the total human effort required, since one solo developer's finite evenings/weekends hours are
the bottleneck regardless of how the categories are sequenced. ~9–13 weeks of actual focused
work, fitted around a full-time job's realistic weekly available hours rather than a full-time
work-week, is what spreads out to ~3–5 months of real calendar time.

**Conservative framing:** given this project's consistent pattern of underestimating
frontend/documentation-shaped work once actually started, treat the upper bound of both figures
(13 weeks effort-time / 5 months calendar-duration) as the more likely real outcome, not the
lower bound.

---

## 5. Recommended Execution Sequence

Adjusted from the previously discussed shape based on the actual dependency map in §3 — B and
D do not need to wait for A, so the sequence below runs them alongside it rather than after it.

1. **Milestone 0 — Backlog approval** (this document; gate before any implementation starts).
2. **Milestone 1 — Frontend foundation** (A1, A2): design tokens + reusable component system.
   *Runs alongside* **Milestone 1b — Docs site foundation** (B1, B2) and **Milestone 1c —
   Packaging quick wins** (D1–D3) — both are independent of A and cheap; doing them now avoids
   a purely serial 13-week chain.
3. **Milestone 2 — Core frontend modernization** (A3–A8, A15, A22–A25): shell, routing,
   responsive design, onboarding + API-key management, `remark-gfm`, loading/empty/error
   states, accessibility baseline. Depends on Milestone 1.
4. **Milestone 3 — Frontend feature-page restyle** (A9–A21, A26): apply the Milestone-1
   component system to the already-functionally-complete agent/observability/evals pages, then
   a final consistency pass. Can overlap the tail of Milestone 2.
5. **Milestone 4 — Developer documentation content** (B4–B16, C4): adapt existing strong
   material into the Milestone-1b site structure; write the genuinely new first-workflow and
   extension-point content. Runs mostly independently of Milestones 2–3.
6. **Milestone 5 — Developer experience polish** (C1, C2, C3, C5, C6): cross-link the now
   largely-finished frontend (Milestone 2–3) and docs site (Milestone 4); replace
   `frontend/README.md`; write the explicit project-relationship map. This is the first
   milestone that genuinely needs both A and B substantially done.
7. **Milestone 6 — Deployment/demonstration coherence** (E1, E2): verify the live demo
   instance; assemble the end-to-end discover-to-extend narrative across everything built so
   far.
8. **Milestone 7 — Final integration and QA**: live, real end-to-end verification across the
   finished frontend, docs site, and existing backend — per this project's own established
   practice of proving things live rather than trusting review alone.
9. **Completed OrchestAI product-completion backlog.**

No milestone in this sequence requires reopening the auth model, the product direction, or the
engineering-foundation completeness — all three stay fixed throughout, per §0.

---

## 6. Open Items Requiring a Decision Before Related Work Starts

Carried forward, not resolved here:

- **E3 (public read-only demo mode):** genuinely new scope, not assumed into Must Build, and
  **not required to declare this backlog complete.** Remains DEFER; if you want it considered
  at all, it should be raised later as its own separate product decision (weighing the added
  infrastructure and security surface), not folded into this backlog's completion criteria.
- **E1's live-instance freshness:** needs a real check at execution time (curl/browse the
  existing Railway/Vercel URLs), not assumed from this planning pass.

---

*No implementation performed as part of producing this document. No other repository files
were modified. Awaiting review and explicit approval before Milestone 1 begins.*
