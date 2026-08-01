// frontend/src/components/EmptyState.jsx
//
// R1 Task 5 (master plan §5 "Add new shared primitives the redesign needs but
// doesn't yet have... at least an `EmptyState` component") — a NEW shared
// primitive, not a migration of an existing one. Retires the "one sentence in
// a void" pattern currently scattered across pages as bare `StateText`/muted-
// span one-liners (e.g. MemoriesPage's "No memories saved yet…", Observability's
// "Select a task to view its execution timeline."): those remain valid for a
// transient loading/error caption, but a genuine empty section deserves real
// orientation and weight — a heading, optional supporting prose, and an
// optional next action — not a single low-emphasis line.
//
// This is a dense, dark engineer's console (DESIGN.md's "flat by default,
// One Accent Rule, never a consumer/marketing surface" identity) — so this is
// deliberately QUIET: a short heading, restrained muted-tier colors
// (`colors.text` for the heading, `colors.subtext0` for the description —
// both individually verified >=6.2:1 contrast against every surface tier per
// tokens.js's surface-hierarchy comment — never a bright/celebratory
// treatment), centered composition, generous but not decorative vertical
// rhythm (`spacing.xxxl`/`xl` padding, `spacing.sm` gaps). No illustration
// system, no marketing copy — an optional caller-supplied `icon` node is the
// only visual embellishment, and it renders muted (`colors.overlay0`, this
// token's documented "small decorative... text" role per tokens.js) and
// `aria-hidden` since it is decorative, not informational.
//
// API: `EmptyState({ title, description, action, icon, style })`
//   - `title` (required): short heading string, rendered as a real `<h3>`
//     element — semantically a heading so assistive tech can navigate to it,
//     `typography.title` face (14px/700, UI face) rather than the louder
//     `typography.heading` (20px) role: `heading` is reserved for the
//     once-per-view page title (per tokens.js's own R1 Task 3 comment), and a
//     20px headline inside an empty *section* would read as louder than the
//     page chrome around it, undermining the "quiet, oriented" intent above.
//   - `description` (optional): supporting prose, `typography.body` (13px,
//     DATA face) — matches every other primitive's use of `body` for prose
//     that isn't UI chrome; this is plain sentence-case text, not a
//     data/code value, but `body` is the only prose-appropriate role tokens.js
//     exposes today (`heading`/`title`/`label` are all UI-chrome-only per
//     their own documented roles) and no per-element UI/data split exists yet
//     (that is explicitly deferred to Phase 2 — see tokens.js's `body` note).
//   - `action` (optional): a fully-formed React node (typically a `<Button>`)
//     rendered below the description — this component does not know how to
//     build the action itself, only where to place it.
//   - `icon` (optional): a leading decorative visual node (svg/emoji/glyph),
//     rendered above the heading, `aria-hidden` and muted-tinted.
//   - `style` (optional): merges LAST, overriding every computed property
//     above — matches every other primitive's override pattern (Button,
//     Input, Label, Panel, StatusBadge, StateText).
//
// P2-M6.5 — `headingLevel` (default 2) ADDED. The note below correctly said to
// wait for a real call site; axe supplied one. Empty states sit directly under
// the per-view page `<h1>`, so a hardcoded `<h3>` skipped a level and tripped
// axe's heading-order rule on the Playground and Evals surfaces. The default is
// therefore `<h2>`; pass a different level where an empty state is nested under
// a deeper section heading. Level affects the ELEMENT only — every visual
// property here is inline, so the rendering is identical at any level.
//
// Deliberately NOT added (scope decisions):
//   - no built-in `action` button — kept a plain node so this component never
//     needs to know about `Button`'s variants/handlers.
//
// Accessible: `title` is a real heading element (screen-reader-navigable);
// `icon` is `aria-hidden` since it is purely decorative and its meaning (if
// any) is already carried by `title`/`description`.

import { colors, spacing, typography, uiFontFamily } from '../theme/tokens'

export function EmptyState({ title, description, action, icon, style, headingLevel = 2 }) {
  const Heading = `h${headingLevel}`
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        textAlign: 'center',
        gap: spacing.sm,
        padding: `${spacing.xxxl} ${spacing.xl}`,
        ...style,
      }}
    >
      {icon && (
        <div aria-hidden="true" style={{ color: colors.textMuted, display: 'flex' }}>
          {icon}
        </div>
      )}
      <Heading
        style={{
          margin: 0,
          fontFamily: typography.title.fontFamily,
          fontSize: typography.title.fontSize,
          fontWeight: typography.title.fontWeight,
          lineHeight: typography.title.lineHeight,
          letterSpacing: typography.title.letterSpacing,
          color: colors.text,
        }}
      >
        {title}
      </Heading>
      {description && (
        <p
          style={{
            margin: 0,
            maxWidth: 360,
            // P2-M2: the description is explanatory prose → UI face (IBM Plex
            // Sans), not the mono/data `typography.body` face. Keep body's
            // size/weight/line-height; only the face is corrected. (A2 migrated
            // StateText prose to the UI face; EmptyState's description is the
            // same category and was completed here.)
            fontFamily: uiFontFamily,
            fontSize: typography.body.fontSize,
            fontWeight: typography.body.fontWeight,
            lineHeight: typography.body.lineHeight,
            color: colors.subtext0,
          }}
        >
          {description}
        </p>
      )}
      {action && <div style={{ marginTop: spacing.sm }}>{action}</div>}
    </div>
  )
}
