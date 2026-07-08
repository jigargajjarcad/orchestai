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
typography:
  heading:
    fontFamily: "'JetBrains Mono', 'Fira Code', monospace"
    fontSize: "20px"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "normal"
  body:
    fontFamily: "'JetBrains Mono', 'Fira Code', monospace"
    fontSize: "13px"
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: "normal"
  label:
    fontFamily: "'JetBrains Mono', 'Fira Code', monospace"
    fontSize: "11px"
    fontWeight: 700
    lineHeight: 1.2
    letterSpacing: "0.07em"
rounded:
  sm: "3px"
  md: "4px"
  lg: "6px"
  xl: "8px"
spacing:
  xs: "4px"
  sm: "8px"
  md: "12px"
  lg: "16px"
  xl: "24px"
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
reproduce it. The visual language follows from that job directly: monospace throughout (this
is a tool that shows raw traces, timestamps, and span IDs — a proportional font would be
lying about what the data actually is), a single Catppuccin Mocha dark base with zero
elevation, and a deliberately narrow, dense information architecture that packs execution
data close together rather than spacing it out for comfort.

This system explicitly rejects the generic SaaS dashboard: no gradient hero metrics, no
cream/sand card grids, no rounded pastel "consumer analytics" look. An engineer debugging a
failed multi-agent run at 2am should feel like they're reading `htop` or a Grafana Tempo
trace view, not browsing a Stripe-dashboard-clone.

**Key Characteristics:**
- Monospace-only typography, no serif or humanist sans anywhere
- Flat throughout — zero shadows, depth via background-color layering only
- Dense: small type scale (11–14px for 90% of UI), tight padding, small radii (3–8px)
- Two distinct color roles never blended: soft Catppuccin tones identify *what* (agent type,
  structural UI), harder saturated tones identify *state* (status badges only)

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
spans, summary cards, comparison side panels). Never reassign these.
- **Beacon Yellow** (`#f9e2af`): Orchestrator
- **Trace Blue** (`#89b4fa`): Research
- **Signal Green** (`#a6e3a1`): Writer
- **Agent Mauve** (`#cba6f7`): Code (also used for tool-call spans generically)
- **Agent Sky** (`#89dceb`): Data
- **Agent Peach** (`#fab387`): Browser

### Neutral
- **Text** (`#cdd6f4`): primary reading text, all body copy and values.
- **Subtext0** (`#a6adc8`): secondary text — descriptions, output previews, less critical copy.
- **Overlay0** (`#6c7086`): muted labels, section eyebrows, icons, timestamps.
- **Surface2** (`#585b70`): dimmest text tier, used sparingly (chart axis labels).
- **Surface0** (`#313244`): all borders and dividers.
- **Mantle** (`#181825`): recessed backgrounds — nested content inside a panel (message
  bubbles, tool-call rows, code blocks).
- **Base** (`#1e1e2e`): the standard panel/card background.
- **Crust** (`#11111b`): the page background, the darkest layer.

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
text — the two systems read as different signals and mixing them erodes that distinction.

**The One Accent Rule.** Trace Blue is the only color used for interactive affordances
(buttons, links, focus). It appears on a small fraction of any screen; its rarity is what
makes it register as "click here" rather than decoration.

## 3. Typography

**Display Font:** 'JetBrains Mono', 'Fira Code', monospace
**Body Font:** 'JetBrains Mono', 'Fira Code', monospace
**Label/Mono Font:** same family — this system has one typeface, not a pairing.

**Character:** A single monospace family carries the entire interface, from page headings
down to inline span IDs. There is no display/body contrast to manage — hierarchy comes
entirely from size, weight, and color, the way a well-organized terminal UI establishes
hierarchy without ever leaving fixed-width type.

### Hierarchy
- **Heading** (700, 20px, 1.2 line-height): page title only ("OrchestAI"), used once per view.
- **Title** (700, 14–15px, 1.3): section-level headers inside a panel (e.g. "Waiting for Your
  Approval").
- **Body** (400, 12–13px, 1.5): the default for prompts, outputs, table cells, descriptions.
- **Label** (700, 11px, 0.07em letter-spacing, uppercase): section eyebrows and column
  headers ("TASK TITLE", "AGENT ERROR RATES"). Always paired with `overlay0` or `surface2`
  color, never full-brightness text.
- **Micro** (9–10px): chart axis labels and the smallest secondary annotations only.

### Named Rules
**The No-Serif Rule.** Nothing in this system is ever set in a serif or humanist sans. If a
new screen needs a font decision, the answer is always the existing monospace stack.

## 4. Elevation

Flat by design — there is no `box-shadow` anywhere in the codebase. Depth is conveyed
entirely through background-color layering across three fixed steps (crust → base → mantle,
darkest to lightest-of-the-three) and 1px `surface0` borders. A panel sits on `base` against
the `crust` page; content nested inside that panel (a message, a tool-call row, a code block)
drops to `mantle`, one step darker again, so the eye reads containment from luminance alone.

### Named Rules
**The Flat-By-Default Rule.** Surfaces never cast shadows. The only "elevation" cue besides
background layering is a colored left-border (3px, `border-left`) on status-carrying cards
(agent execution cards, approval prompts, error banners) — a deliberate, singular exception
to the general "no side-stripe borders" rule, used only to attach a status color to a card's
identity at a glance, never as decoration.

## 5. Components

Every component favors information density over breathing room: small radii, tight padding,
text sized to fit as much real data on screen as possible without feeling cramped.

### Buttons
- **Shape:** 6px radius (`rounded.lg`).
- **Primary:** Trace Blue background, Crust text, 700 weight, 9px vertical / full-width
  padding, 13px label. Disabled state drops to `surface0` background with `overlay0` text.
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
  padding, no visible focus ring beyond the browser default (this system has not yet defined
  a custom focus treatment — flag if accessibility work adds one).
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

## 6. Do's and Don'ts

### Do:
- **Do** keep every screen monospace — headings, labels, body, values, all one family.
- **Do** use the Two-Palette Rule: Catppuccin tones for identity, hard Status tones for state.
- **Do** use tinted-pill badges (20% alpha bg, 50–60% alpha border, full-opacity text) for any
  new status indicator, matching the existing badge pattern exactly.
- **Do** reserve the 3px `border-left` accent for cards that represent a single dominant
  status; it is the one deliberate exception to the side-stripe ban below.
- **Do** keep type sizes in the 11–14px range for standard UI; reserve 18–20px for the single
  page heading only.

### Don't:
- **Don't** introduce a gradient, hero metric, or cream/sand card grid — this is a tracing
  tool, not a marketing-adjacent SaaS dashboard.
- **Don't** use `border-left` as a colored accent on anything other than a status-carrying
  card (agent execution card, approval card, error banner, manager-review card). It is not a
  general decorative device.
- **Don't** add a `box-shadow` anywhere. Depth is background-layering only.
- **Don't** mix Catppuccin soft tones and hard Status tones on the same semantic concept
  (e.g. never color a "Failed" status badge with Alert Red `#f38ba8` — use Status Failed
  `#dc2626`).
- **Don't** introduce a second typeface, even for "just one heading." The mono-only rule has
  no exceptions.
- **Don't** reconstruct span parent/child hierarchy from timestamps or visual proximity —
  it must always be driven by the actual `spanId`/`parentSpanId` data, mirroring the backend's
  own architectural rule (ADR-011).
