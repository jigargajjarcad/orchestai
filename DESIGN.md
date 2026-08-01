---
name: OrchestAI
description: LangSmith-style observability dashboard for a .NET multi-agent AI orchestration framework
colors:
  crust: "#11111b"
  base: "#1e1e2e"
  mantle: "#181825"
  surface0: "#313244"
  surface2: "#585b70"
  overlay0: "#6c7086"
  subtext0: "#a6adc8"
  text: "#cdd6f4"
  trace-blue: "#89b4fa"
  signal-green: "#a6e3a1"
  alert-red: "#f38ba8"
  beacon-yellow: "#f9e2af"
  agent-mauve: "#cba6f7"
  agent-sky: "#89dceb"
  agent-peach: "#fab387"
  status-running: "#2563eb"
  status-completed: "#16a34a"
  status-failed: "#dc2626"
  status-pending: "#6b7280"
  status-warning: "#f59e0b"
  error-bg: "#2d1b1b"
  surface-raised: "#232336"
  surface-overlay: "#2a2a40"
semantic:
  good: "{colors.status-completed}"
  warn: "{colors.status-warning}"
  critical: "{colors.status-failed}"
typography:
  uiFontFamily: "'IBM Plex Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif"
  dataFontFamily: "'JetBrains Mono', 'Fira Code', monospace"
  heading:
    fontFamily: "{typography.uiFontFamily}"
    fontSize: "20px"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "normal"
  title:
    fontFamily: "{typography.uiFontFamily}"
    fontSize: "14px"
    fontWeight: 700
    lineHeight: 1.3
    letterSpacing: "normal"
  body:
    fontFamily: "{typography.dataFontFamily}"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  label:
    fontFamily: "{typography.uiFontFamily}"
    fontSize: "11px"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "0.07em"
rounded:
  sm: "3px"
  md: "4px"
  lg: "6px"
  xl: "8px"
  xxl: "12px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
  xxl: "32px"
  xxxl: "48px"
elevation:
  flat: "none"
  raised: "0 1px 2px rgba(0, 0, 0, 0.4), 0 4px 12px rgba(0, 0, 0, 0.35)"
  overlay: "0 8px 30px rgba(0, 0, 0, 0.5)"
components:
  status-badge:
    backgroundColor: "{colors.status-running}"
    textColor: "{colors.status-running}"
    rounded: "{rounded.md}"
    padding: "2px 10px"
  panel:
    backgroundColor: "{colors.base}"
    textColor: "{colors.text}"
    rounded: "{rounded.xl}"
    padding: "16px 18px"
  button-primary:
    backgroundColor: "{colors.trace-blue}"
    textColor: "{colors.crust}"
    rounded: "{rounded.lg}"
    padding: "9px 0"
---

# Design System: OrchestAI

## 1. Overview

**Creative North Star: "The Trace Console"**

OrchestAI's frontend is not a dashboard you browse — it's a console you read. Every screen
exists to answer one question fast: what happened, why, how much did it cost, how do I
reproduce it. The visual language follows from that job directly: a **two-face type system**
(a proportional UI face for chrome/prose, monospace reserved for the raw data — IDs,
timestamps, durations, cost figures, code/JSON — because a proportional font really would be
lying about what *that* content is), a Catppuccin Mocha dark base with a genuine, restrained
**surface hierarchy** and a small disciplined **elevation** scale, and a deliberately narrow,
dense information architecture that packs execution data close together rather than spacing
it out for comfort.

This system explicitly rejects the generic SaaS dashboard: no gradient hero metrics, no
cream/sand card grids, no rounded pastel "consumer analytics" look. An engineer debugging a
failed multi-agent run at 2am should feel like they're reading `htop` or a Grafana Tempo
trace view, not browsing a Stripe-dashboard-clone. The redesign (R1, "Design-System
Foundation") sharpens this identity — real type hierarchy, real surface depth, restrained
shadows — it does not soften it toward generic SaaS.

**Key Characteristics:**
- **Two-face typography**: IBM Plex Sans (self-hosted) for UI chrome — headings, section
  titles, labels, navigation, prose — JetBrains Mono (self-hosted) reserved for data — IDs,
  timestamps, durations, cost figures, code/JSON, span IDs. Validated first in the
  Observability Timeline slice, now the product-wide rule.
- **Layered, not flat**: a 4-tier surface ramp (`canvas` → `surface` → `raised` → `overlay`)
  and a small 3-step elevation scale (`flat` → `raised` → `overlay`) replace the old
  two-flat-layer/zero-shadow model. Still restrained — soft, dark-mode-appropriate shadows
  only, never a dramatic drop-shadow.
- Dense: small type scale (10–20px, spanning display→caption), tight padding, small-to-medium
  radii (3–12px)
- Two distinct color roles never blended: soft Catppuccin tones identify *what* (agent type,
  structural UI), harder saturated tones identify *state* (status badges, and — via
  documented alias, not new hues — non-status semantic UI like toasts/banners)

## 2. Colors

A single dark Catppuccin Mocha base, layered by background lightness rather than shadow, with
two non-overlapping accent systems: soft tones for identity, hard tones for state.

### Primary
- **Trace Blue** (`#89b4fa`): the one accent used for interactive/informational emphasis —
  links, the Research agent's color, primary buttons, trace ID display. Sparse by design.

### Secondary
- **Signal Green** (`#a6e3a1`): success states, positive cost/savings figures, "memory used"
  and other affirmative indicators.
- **Alert Red** (`#f38ba8`): the *soft* error tone — inline error banners, failed tool-call
  text, form validation. Distinct from Status Failed below; this is content-level alerting,
  not a state badge.

### Tertiary — Agent Identity Palette
Each agent type gets one fixed, memorable color used consistently across every view (timeline
spans, summary cards, comparison side panels). Never reassign these. Validated at realistic
density (R1 Task 1, `docs/superpowers/plans/2026-07-26-r1-palette-decision.md`): a dense
47-span synthetic fixture with repeated identities, all statuses, 16-way fan-out and depth-5
nesting confirmed the palette stays legible and hierarchical — **keep exactly as-is, do not
expand.**
- **Beacon Yellow** (`#f9e2af`): Orchestrator
- **Trace Blue** (`#89b4fa`): Research
- **Signal Green** (`#a6e3a1`): Writer
- **Agent Mauve** (`#cba6f7`): Code (also used for tool-call spans generically)
- **Agent Sky** (`#89dceb`): Data
- **Agent Peach** (`#fab387`): Browser

**Two accepted proximities (documented, not drift):**
- **Research (Trace Blue) vs. Data (Agent Sky)** are the closest identity pair (redmean ~83).
  Real but distinguishable at density (saturated periwinkle vs. lighter cyan); rarely adjacent
  in a real trace. Identity-preservation wins — accepted as-is.
- **Tool-call nodes share Code's exact mauve** regardless of which agent dispatched the call —
  an intentional existing convention (matches `SpanRow`'s `spanType === 'ToolCall'` rule).
  Differentiated by node shape + 🔧 label + smaller size, not color alone. Accepted as-is.

### Neutral
- **Text** (`#cdd6f4`): primary reading text, all body copy and values.
- **Subtext0** (`#a6adc8`): secondary text — descriptions, output previews, less critical copy.
  Safe (≥6.2:1) against every surface tier, including the new `raised`/`overlay` tiers below.
- **Overlay0** (`#6c7086`): muted, *decorative-only* labels/eyebrows/icons — never body-weight
  content. Already below the 4.5:1 body-text bar against `crust`/`base` (3.84 / 3.36) and
  further still against `raised`/`overlay` (3.15 / 2.86); use `subtext0` for anything that must
  be read as content.
- **Surface2** (`#585b70`): dimmest text tier, used sparingly (chart axis labels).
- **Surface0** (`#313244`): all borders and dividers; also the documented `disabled` background
  for buttons/inputs (see Interactive States below).
- **Mantle** (`#181825`): recessed backgrounds — nested content inside a panel (message
  bubbles, tool-call rows, code blocks).
- **Base** (`#1e1e2e`): the standard panel/card background.
- **Crust** (`#11111b`): the page background, the darkest layer.

### Surface Hierarchy (R1 addition)
A 4-tier layered model — generalized from the Observability Timeline slice's validated
`surface.card`/`cardHover` tones — replacing the old two-flat-layer (`crust`/`base`) model with
genuine surface presence:
- **Canvas** (`#11111b`, = Crust): the page background.
- **Surface** (`#1e1e2e`, = Base): the resting panel/card background — unchanged from before.
- **Raised** (`#232336`): hover/active/selected surfaces — one step up from `surface`, giving
  interactive cards more presence.
- **Overlay** (`#2a2a40`): drawers/modals/menus — the deepest, most prominent tier.

All four tiers keep `text` (`#cdd6f4`, ≥9.6:1) and `subtext0` (`#a6adc8`, ≥6.2:1) safely above
the 4.5:1 body-text contrast bar. A hairline border (`rgba(205, 214, 244, 0.08)`) and a modal
scrim (`rgba(0, 0, 0, 0.35)`) round out the ramp for `raised`/`overlay` surfaces.

### Semantic tokens (R1 addition)
For non-status semantic UI — a success toast, a validation-warning banner, a
destructive-confirmation accent, i.e. meaning that isn't a span's execution status but still
reads as good/warn/critical — `good`/`warn`/`critical` **alias the existing Status hues**
(green/amber/red below) rather than introducing new colors, per the R1 Task 1 palette-density
decision. This keeps the palette anchored: no decorative color is ever added merely for
variety.
- `good` → Status Completed (`#16a34a`)
- `warn` → Status Warning (`#f59e0b`)
- `critical` → Status Failed (`#dc2626`)

### Status — a separate, harder palette
- **Status Running** (`#2563eb`), **Status Completed** (`#16a34a`), **Status Failed**
  (`#dc2626`), **Status Pending** (`#6b7280`), **Status Warning** (`#f59e0b`, retries /
  checkpoint-restored indicators): a distinctly more saturated set than the Catppuccin
  palette above, used *exclusively* for execution/task status badges and their `20%`-alpha
  background tints. `error-bg` (`#2d1b1b`) is the matching dark-red banner background for
  error messages.

### Named Rules
**The Two-Palette Rule.** Identity (which agent, which color role in the UI) is always a
Catppuccin tone. State (is this run running / done / failed) is always drawn from the
harder Status set. Never use Alert Red for a "Failed" badge or Status Failed for inline error
text — the two systems read as different signals and mixing them erodes that distinction. The
`good`/`warn`/`critical` semantic aliases above are Status-set colors reused under a
non-status name for non-status UI — they do not create a third palette.

**The One Accent Rule.** Trace Blue is the only color used for interactive affordances
(buttons, links, focus). It appears on a small fraction of any screen; its rarity is what
makes it register as "click here" rather than decoration. (Considered and explicitly kept at
R1: the redesign investigation weighed broadening this and instead validated — via the dense
palette-density stress test — that the existing discipline already reads as legible and
intentional; see the palette-decision doc.)

## 3. Typography

**UI Font:** 'IBM Plex Sans', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif
**Data Font:** 'JetBrains Mono', 'Fira Code', monospace

**Character:** Two families, by role (R1 redesign — supersedes the old mono-only system).
IBM Plex Sans carries UI chrome — headings, section titles, labels, navigation, prose,
empty-state copy. JetBrains Mono is reserved for data — IDs, timestamps, durations, cost
figures, code/JSON, span IDs — content where a proportional font would misrepresent what the
data actually is. This is the Observability Timeline slice's validated split, generalized
product-wide. Both faces are self-hosted (subsetted WOFF2, `font-display: swap`, no external
CDN, OFL-1.1 licensed); JetBrains Mono was, before R1, only a font-family *name* relying on the
visitor having it installed locally.

Chosen as a deliberate fit for OrchestAI's technical/developer-oriented product context —
JetBrains Mono is already established in the product's identity, so IBM Plex Sans is a
coherent companion (both are engineered for developer tooling), not an unrelated type system.
This is a product-positioning choice, not a claim that system fonts are inherently less
credible.

### Type Scale
A single documented scale spans display → caption, each step annotated with which face it
uses (see `theme/typography.js`'s `typeScale` for the full, sourced table):
`display` (28px/700) → `heading` (20px/700) → `subheading` (15px/600) → `title` (14px/600) →
`body` (13px/400) → `label` (11px/500) → `caption` (10px/500) — all UI face — with matching
`dataValue`/`dataBody`/`dataLabel`/`dataCaption` mono counterparts sized to the same visual
rank, so a UI label and an adjoining data value (e.g. a stat tile's caption + figure) line up
visually even though they render in different faces.

### Hierarchy (legacy 4-role set — still the primary export every component reads)
R1 reassigns which face each of these four roles carries, without changing any size/weight/
line-height — a **token-value change, not a per-element redesign** (that per-element split to
the finer `typeScale` above is Phase 2 work):
- **Heading** (700, 20px, 1.2 line-height, **UI face**): page title only ("OrchestAI"), used
  once per view. Never renders a data value — safe to move off mono.
- **Title** (700, 14–15px, 1.3, **UI face**): section-level headers inside a panel (e.g.
  "Waiting for Your Approval"). Never renders a data value — safe to move off mono.
- **Body** (400, 12–13px, 1.5, **data face — unchanged**): the default for prompts, outputs,
  table cells, descriptions. Deliberately kept on the mono face: this role is the most
  data-adjacent of the four (raw prompts/outputs/table cells can be exactly the
  monospace-sensitive content a UI font would misrepresent), so it stays put until Phase 2
  does the real per-element split.
- **Label** (700, 11px, 0.07em letter-spacing, uppercase, **UI face**): section eyebrows and
  column headers ("TASK TITLE", "AGENT ERROR RATES") — never the data value itself. Always
  paired with `textMuted` color, never full-brightness text. (F1: was `overlay0` or
  `surface2` — both are SURFACE-ramp steps measuring 3.36-3.84:1 and 2.81:1, below the
  4.5:1 WCAG AA bar for body text. `textMuted` (#9399b2) is the lowest neutral-ramp step
  clearing 4.5:1 against all four surface tiers. `overlay0`/`surface2` keep their values
  and remain correct for borders, fills and disabled states.)
- **Micro** (9–10px): chart axis labels and the smallest secondary annotations only.

### Named Rules
**The Two-Face Rule.** Supersedes the old No-Serif Rule. Exactly two typefaces exist in this
system, split by role: IBM Plex Sans for UI chrome, JetBrains Mono for data. Never introduce a
third typeface. Never render IDs, timestamps, durations, cost figures, or code/JSON in the UI
face — that content must always be monospace, regardless of which role's token happens to
carry it at a given point in the rollout.

**Canonical element → face mapping (P2-M2's governing rule; do not re-litigate).** Every
element in the product resolves to exactly one of these two lists:
- **UI face (IBM Plex Sans):** navigation, headings, section titles, labels, buttons,
  instruction/prose copy, status messages, chrome of every kind.
- **Data face (JetBrains Mono):** IDs, timestamps, durations, cost figures, any numeric value,
  code/JSON, technical IDs, trace/span IDs, data table cells.

**Four standing decisions (approved, encoded by P2-M2 Task A1):**
1. The app-shell default (`#root` in `index.css`) stays JetBrains Mono — it is the data-safe
   fallback for any element not yet explicitly assigned a role, and Task A1 does not touch it.
2. `typography.body` (`theme/tokens.js`) stays on the data face (`dataFontFamily`), unchanged —
   see the Hierarchy note above; it remains the correct default for prompts/outputs/table cells
   until Phase 2's per-element split reassigns individual call sites.
3. UI prose (instructions, empty-state copy, status text) renders via a UI-face role/token
   (`typeScale.body` or an equivalent UI role) — never the legacy mono `typography.body` — once
   a component is migrated to consume it.
4. Within an `Input`/`TextArea`, the **field content** (what the user typed/what's displayed —
   often IDs, numbers, code) stays mono; the **label** above it is UI face. The two never share
   a font-family even though they sit in the same control.

> **Rollout note (R1 partial-baseline → P2-M2 completing the split).** The §5 component CSS
> samples and `.impeccable/design.json`'s component catalog depict each control's **intended
> end-state face** per the Two-Face Rule (e.g. a button label in IBM Plex Sans). At the R1
> partial-baseline the only shared controls that actually rendered the UI face were those
> consuming the `label` role token (`Label`, `StatusBadge`) plus the new role-authored
> primitives; every other control — including bare headings — inherited the JetBrains Mono
> app-shell face (`#root` in `index.css`, plus `index.css`'s bare `h1, h2` rule).
>
> **P2-M2 is now closing that residual-mono baseline, one task at a time.** Task A1 (this task)
> flips bare `h1`/`h2` headings to the UI face (`index.css`'s new `--ui` var) — a heading is,
> by this document's own Hierarchy definition, never a rendered data value, so it's safe to move
> off mono first and in isolation. `#root`'s JetBrains Mono default is deliberately left
> unchanged: it remains the data-safe fallback for every element not yet migrated. The remaining
> mono-inheriting shared controls (`Button`, `NavItem`, `Input`/`TextArea`, `StateText`, and
> `Panel`/`Card` chrome) still inherit `#root`'s mono face today — migrating each to its correct
> role/face is **Task A2** (shared components), with **Task B** completing the sweep
> per-surface (page-by-page). So a sample showing the UI face on a button label still describes
> the target, not yet the rendering, until A2 lands — this is documented "residual mono UI
> text" narrowing in scope task-by-task, not an implementation defect.

## 4. Elevation

A small, disciplined 3-step scale (R1 redesign — supersedes the old zero-shadow model),
generalized from the Observability Timeline slice's validated node/panel shadows. Depth is
still conveyed primarily through background-color layering (the Surface Hierarchy above) and
1px `surface0` borders; elevation adds a restrained shadow on top for genuinely raised/
overlaid surfaces. Deliberately understated — this is a dense technical instrument panel, not
a consumer app with dramatic drop-shadows:
- **Flat** (`none`): the resting state for the large majority of surfaces — page chrome,
  resting panels/cards. This is still the default.
- **Raised** (`0 1px 2px rgba(0,0,0,0.4), 0 4px 12px rgba(0,0,0,0.35)`): hover/active/selected
  cards and the `raised` surface tier.
- **Overlay** (`0 8px 30px rgba(0,0,0,0.5)`): drawers, modals, menus — the `overlay` surface
  tier.

A panel sits on `surface` (`base`) against the `canvas` (`crust`) page; content nested inside
that panel (a message, a tool-call row, a code block) drops to `mantle`, one step darker
again, so the eye still reads containment from luminance alone, exactly as before — elevation
is additive, not a replacement for that layering.

### Named Rules
**The Disciplined-Elevation Rule.** Supersedes the old Flat-By-Default Rule. Every shadow
used in the product must be one of the three `elevation` scale steps above — no arbitrary or
decorative drop-shadows, no dramatic depth. The colored left-border (3px, `border-left`) on
status-carrying cards (agent execution cards, approval prompts, error banners) remains a
separate, still-valid convention — a deliberate, singular exception to the general "no
side-stripe borders" rule, used only to attach a status color to a card's identity at a
glance, never as decoration.

## 5. Components

Every component favors information density over breathing room: small radii, tight padding,
text sized to fit as much real data on screen as possible without feeling cramped. Radius now
maps consistently to element class (`theme/tokens.js`'s `radiusForElement`): inputs/buttons →
`lg` (6px), pills/badges → `md` (4px), cards → `xl` (8px), nested rows → `lg` (6px), big
surfaces (canvas frames, large overlays/modals) → `xxl` (12px, new — the redesign's one
larger-radius addition, matching the Observability slice's 10–14px canvas/node treatment).
Spacing keeps the existing 4/8/12/16/24 scale for standard density, extended at the top end
with `xxl` (32px) / `xxxl` (48px) for more generous page-level rhythm where a surface calls
for it — density stays the default, the extension is intentional rhythm, not uniform tightening.

### Buttons
- **Shape:** 6px radius (`rounded.lg`).
- **Primary:** Trace Blue background, Crust text, 700 weight, 9px vertical / full-width
  padding, 13px label. Disabled state drops to `surface0` background with `overlay0` text
  (see Interactive States, §4 — this is the fixed-neutral disabled convention, not an alpha
  tint).
- **Secondary / Ghost:** transparent background, 1px colored border at 50% alpha (e.g.
  `#dc262680` for a destructive ghost action), text matches the border's base color.

### Status Badges
- **Shape:** 4px radius, `2px 10px` padding, 11px uppercase label text, 700 weight,
  `0.05–0.06em` letter-spacing.
- **Style:** background is the status color at ~18–20% alpha, border is the status color at
  ~50–60% alpha, text is the full-opacity status color. This "tinted pill" pattern is the
  only way status color appears — never a solid fill.

### Cards / Panels
- **Corner Style:** 8px radius (`rounded.xl`) for top-level panels, 4–6px for nested rows.
- **Background:** `base`, dropping to `mantle` one level deeper.
- **Border:** 1px `surface0`, or a status-tinted 1px border at 40% alpha when the panel
  itself represents a status (an agent execution card border-tinted by its own status color).
- **Left accent:** 3px colored `border-left` on any card that carries a single dominant status
  (agent cards, approval card, manager-review card) — see Elevation's named exception.
- **Internal Padding:** `12px 14px` to `16px 18px` depending on density need.

### Inputs / Fields
- **Style:** `surface0` 1px border, `base`-tinted-darker (`#181825`) background, 7–10px
  padding, 6px radius (`radiusForElement.input`).
- **Focus:** a 2px solid Trace Blue outline, 2px offset, `:focus-visible`-scoped (mouse/touch
  activation does not show a ring — only keyboard/programmatic focus does). This is now a
  defined, product-wide convention (see Interactive States, §4) — not an open question.
- **Labels:** always the Label typography style directly above the field, never inline
  placeholder-only.

### Navigation
- **Style:** flat text buttons in a horizontal row, no underline, active state gets a
  `surface0` background fill and brightens text from `overlay0` to `text`. No hover-lift, no
  shadow — background-fill is the only interactive cue.

### Timeline Span Rows (signature component)
The core interaction surface: one row per execution span, indented by nesting depth,
color-dot keyed to agent identity, a horizontal Gantt bar positioned/sized by actual
start-time and duration against the trace's total span, duration and cost right-aligned in
fixed-width columns, status text color-coded from the Status palette. Depth communicates
parent/child relationship (via `paddingLeft`), never inferred visually any other way.

### Interactive States (R1 addition)
A documented state matrix Task 4 applies to every interactive primitive (button, input, nav
item, toggle, selectable card/row, link): default / hover / active / focus-visible / selected
/ disabled / loading.
- **Hover / active / selected**: a background tint over the contextually-relevant identity/
  accent/neutral color, at 8% / 14% / 18% alpha respectively (hex suffixes `14`/`24`/`2e`,
  matching the same alpha-suffix idiom as the status-badge tokens).
- **Disabled**: the already-documented convention (Buttons, above) — solid `surface0`
  background, `overlay0` text. Not an alpha tint; a fixed neutral swap.
- **Focus-visible**: a 2px solid Trace Blue outline, 2px offset — mandatory on every
  interactive element (WCAG AA baseline, `PRODUCT.md`). Matches the ring already shipped for
  `Button`/`Input`/`NavItem` (`index.css`'s `.focus-ring:focus-visible`) and the Observability
  Timeline's canvas-node override; centralized in `theme/tokens.js`'s `focusRing` export as
  the one source of truth for any new JS-driven consumer.

## 6. Do's and Don'ts

### Do:
- **Do** use the UI face (IBM Plex Sans) for chrome — headings, titles, labels, nav, prose —
  and the data face (JetBrains Mono) for IDs/timestamps/durations/costs/code/JSON. Never the
  reverse.
- **Do** use the Two-Palette Rule: Catppuccin tones for identity, hard Status tones for state;
  `good`/`warn`/`critical` are Status-set colors reused for non-status UI, not a third palette.
- **Do** use tinted-pill badges (20% alpha bg, 50–60% alpha border, full-opacity text) for any
  new status indicator, matching the existing badge pattern exactly.
- **Do** reserve the 3px `border-left` accent for cards that represent a single dominant
  status; it is the one deliberate exception to the side-stripe ban below.
- **Do** use the Surface Hierarchy (`canvas`/`surface`/`raised`/`overlay`) for background
  layering and the Elevation scale (`flat`/`raised`/`overlay`) for shadows — together, not
  interchangeably; elevation is additive to the surface tier, not a substitute for it.
- **Do** give every interactive element a visible `:focus-visible` ring (2px solid Trace Blue,
  2px offset) — accessibility is a first-class acceptance criterion, not a final pass.
- **Do** keep type sizes in the 10–20px range for standard UI (the full scale runs
  display(28px)→caption(10px)); reserve 20px+ for the single page heading/display role only.

### Don't:
- **Don't** introduce a gradient, hero metric, or cream/sand card grid — this is a tracing
  tool, not a marketing-adjacent SaaS dashboard.
- **Don't** use `border-left` as a colored accent on anything other than a status-carrying
  card (agent execution card, approval card, error banner, manager-review card). It is not a
  general decorative device.
- **Don't** add a `box-shadow` outside the three documented `elevation` steps (`flat`/
  `raised`/`overlay`) — no arbitrary or dramatic depth.
- **Don't** mix Catppuccin soft tones and hard Status tones on the same semantic concept
  (e.g. never color a "Failed" status badge with Alert Red `#f38ba8` — use Status Failed
  `#dc2626`).
- **Don't** introduce a third typeface, or render data (IDs/timestamps/durations/costs/code/
  JSON) in the UI face — the Two-Face Rule has no exceptions in either direction.
- **Don't** expand the 6-color Agent Identity Palette or the 5-color Status palette — R1's
  dense-fixture stress test validated the current breadth as sufficient; add semantic meaning
  via the documented `good`/`warn`/`critical` aliases, never a new hue.
- **Don't** reconstruct span parent/child hierarchy from timestamps or visual proximity —
  it must always be driven by the actual `spanId`/`parentSpanId` data, mirroring the backend's
  own architectural rule (ADR-011).
