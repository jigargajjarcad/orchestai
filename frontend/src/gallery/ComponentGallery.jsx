// frontend/src/gallery/ComponentGallery.jsx
//
// R1 Task 6 — the Component Gallery: a single, static page rendering the
// ENTIRE new design system (T2-T5) coherently, for the Go/No-Go A human
// visual review. Verification-only — not a product page, not wired into any
// route (see gallery.html / galleryMain.jsx). Every value shown here is read
// from the real token/typography/component modules; nothing is
// re-declared or hand-copied.
//
// Structure (top to bottom, per the task brief):
//   1. Typography scale — every `typeScale` step, both faces, proving both
//      self-hosted fonts actually render (not a system fallback).
//   2. Palette — identity / status / accent / semantic, grouped separately
//      (Two-Palette Rule), plus the T1 dense-palette stress fixture at
//      density via the real, unmodified `DensePaletteHarness`.
//   3. Surface + elevation — the canvas -> surface -> raised -> overlay
//      ramp, each rung carrying its matching elevation shadow.
//   4. Every shared primitive x its documented states.
//
// Gallery chrome (section headings, swatch labels, etc.) reuses the design
// system's own typography/color tokens rather than inventing one-off styles.

import { useState } from 'react'
import {
  colors,
  semantic,
  surface,
  elevation,
  spacing,
  radii,
  typeScale,
  uiFontFamily,
  dataFontFamily,
} from '../theme/tokens'
import { agentColors } from '../observability/timelineTheme'
import { DensePaletteHarness } from '../observability/DensePaletteHarness'
import { Button } from '../components/Button'
import { Input, TextArea } from '../components/Input'
import { Label } from '../components/Label'
import { StateText } from '../components/StateText'
import { StatusBadge } from '../components/StatusBadge'
import { Nav, NavItem } from '../components/NavItem'
import { Panel } from '../components/Panel'
import { EmptyState } from '../components/EmptyState'
import { Toggle } from '../components/Toggle'
import { Card } from '../components/Card'

// ---------------------------------------------------------------------------
// Gallery chrome helpers — presentational only, built from the real tokens.
// ---------------------------------------------------------------------------

function SectionHeading({ index, title, note }) {
  return (
    <div style={{ marginBottom: spacing.lg }}>
      <h2
        style={{
          margin: 0,
          color: colors.text,
          fontFamily: typeScale.display.fontFamily,
          fontSize: typeScale.display.fontSize,
          fontWeight: typeScale.display.fontWeight,
          lineHeight: typeScale.display.lineHeight,
          letterSpacing: typeScale.display.letterSpacing,
        }}
      >
        {index}. {title}
      </h2>
      {note && (
        <p
          style={{
            margin: `${spacing.xs} 0 0`,
            color: colors.subtext0,
            fontFamily: typeScale.body.fontFamily,
            fontSize: typeScale.body.fontSize,
            lineHeight: typeScale.body.lineHeight,
            maxWidth: 720,
          }}
        >
          {note}
        </p>
      )}
    </div>
  )
}

function SubLabel({ children }) {
  return (
    <div
      style={{
        color: colors.overlay0,
        fontFamily: typeScale.label.fontFamily,
        fontSize: typeScale.label.fontSize,
        fontWeight: typeScale.label.fontWeight,
        letterSpacing: typeScale.label.letterSpacing,
        textTransform: 'uppercase',
        marginBottom: spacing.sm,
      }}
    >
      {children}
    </div>
  )
}

function Section({ index, title, note, children }) {
  return (
    <section style={{ marginBottom: spacing.xxxl }}>
      <SectionHeading index={index} title={title} note={note} />
      {children}
    </section>
  )
}

// A labeled row: a `SubLabel` above whatever primitive-state markup follows.
function Row({ label, children }) {
  return (
    <div style={{ marginBottom: spacing.xl }}>
      <SubLabel>{label}</SubLabel>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: spacing.lg, alignItems: 'flex-start' }}>
        {children}
      </div>
    </div>
  )
}

// ---------------------------------------------------------------------------
// 1. Typography
// ---------------------------------------------------------------------------

const TYPE_ROLE_FACE = {
  display: 'IBM Plex Sans / UI',
  heading: 'IBM Plex Sans / UI',
  subheading: 'IBM Plex Sans / UI',
  title: 'IBM Plex Sans / UI',
  body: 'IBM Plex Sans / UI',
  label: 'IBM Plex Sans / UI',
  caption: 'IBM Plex Sans / UI',
  dataValue: 'JetBrains Mono / data',
  dataBody: 'JetBrains Mono / data',
  dataLabel: 'JetBrains Mono / data',
  dataCaption: 'JetBrains Mono / data',
}

const TYPE_SPECIMEN_TEXT = 'The quick brown fox jumps — 0123'

function TypeSpecimenRow({ roleName }) {
  const style = typeScale[roleName]
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'baseline',
        gap: spacing.lg,
        padding: `${spacing.sm} 0`,
        borderBottom: `1px solid ${surface.border}`,
      }}
    >
      <div
        style={{
          width: 220,
          flexShrink: 0,
          fontFamily: uiFontFamily,
          fontSize: 11,
          color: colors.subtext0,
        }}
      >
        <div style={{ fontWeight: 600, color: colors.text }}>typeScale.{roleName}</div>
        <div>{TYPE_ROLE_FACE[roleName]}</div>
        <div style={{ color: colors.overlay0 }}>
          {style.fontSize} / {style.fontWeight}
        </div>
      </div>
      <div
        style={{
          fontFamily: style.fontFamily,
          fontSize: style.fontSize,
          fontWeight: style.fontWeight,
          lineHeight: style.lineHeight,
          letterSpacing: style.letterSpacing,
          color: colors.text,
        }}
      >
        {TYPE_SPECIMEN_TEXT}
      </div>
    </div>
  )
}

function TypographySection() {
  return (
    <Section
      index={1}
      title="Typography scale"
      note="Every typeScale step, both faces. The two lines below use the exact same sample text in each font stack — if only one style of letterforms appears, a face failed to load."
    >
      <div style={{ marginBottom: spacing.xl }}>
        <SubLabel>Side-by-side face comparison (same text, both stacks)</SubLabel>
        <Card level="raised">
          <div style={{ display: 'flex', flexDirection: 'column', gap: spacing.sm }}>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: spacing.md }}>
              <span style={{ width: 160, flexShrink: 0, color: colors.subtext0, fontFamily: uiFontFamily, fontSize: 11 }}>
                IBM Plex Sans (UI)
              </span>
              <span style={{ fontFamily: uiFontFamily, fontSize: 20, fontWeight: 600, color: colors.text }}>
                {TYPE_SPECIMEN_TEXT}
              </span>
            </div>
            <div style={{ display: 'flex', alignItems: 'baseline', gap: spacing.md }}>
              <span style={{ width: 160, flexShrink: 0, color: colors.subtext0, fontFamily: uiFontFamily, fontSize: 11 }}>
                JetBrains Mono (data)
              </span>
              <span style={{ fontFamily: dataFontFamily, fontSize: 20, fontWeight: 400, color: colors.text }}>
                {TYPE_SPECIMEN_TEXT}
              </span>
            </div>
          </div>
        </Card>
      </div>

      <SubLabel>Full scale (typeScale.*)</SubLabel>
      <Card level="surface">
        {Object.keys(typeScale).map((roleName) => (
          <TypeSpecimenRow key={roleName} roleName={roleName} />
        ))}
      </Card>
    </Section>
  )
}

// ---------------------------------------------------------------------------
// 2. Palette
// ---------------------------------------------------------------------------

function Swatch({ name, hex }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: spacing.sm, width: 220 }}>
      <div
        style={{
          width: 32,
          height: 32,
          borderRadius: radii.md,
          background: hex,
          border: `1px solid ${surface.border}`,
          flexShrink: 0,
        }}
      />
      <div style={{ fontFamily: uiFontFamily, fontSize: 12, color: colors.text, lineHeight: 1.3 }}>
        <div style={{ fontWeight: 600 }}>{name}</div>
        <div style={{ fontFamily: dataFontFamily, fontSize: 11, color: colors.subtext0 }}>{hex}</div>
      </div>
    </div>
  )
}

const STATUS_SWATCHES = [
  { name: 'Pending', hex: colors.statusPending },
  { name: 'Running', hex: colors.statusRunning },
  { name: 'Completed', hex: colors.statusCompleted },
  { name: 'Failed', hex: colors.statusFailed },
  { name: 'Warning', hex: colors.statusWarning },
]

const SEMANTIC_SWATCHES = [
  { name: 'semantic.good', hex: semantic.good },
  { name: 'semantic.warn', hex: semantic.warn },
  { name: 'semantic.critical', hex: semantic.critical },
]

function PaletteSection() {
  return (
    <Section
      index={2}
      title="Palette"
      note="Two separate palettes (Two-Palette Rule): Agent Identity colors label WHO is acting, Status colors label WHAT STATE a span is in. They are never blended. Semantic aliases point at the existing status hues, not new colors."
    >
      <Row label="Agent identity (6)">
        {Object.entries(agentColors).map(([name, hex]) => (
          <Swatch key={name} name={name} hex={hex} />
        ))}
      </Row>

      <Row label="Status (5)">
        {STATUS_SWATCHES.map((s) => (
          <Swatch key={s.name} name={s.name} hex={s.hex} />
        ))}
      </Row>

      <Row label="Accent (One Accent Rule)">
        <Swatch name="traceBlue" hex={colors.traceBlue} />
      </Row>

      <Row label="Semantic aliases (good / warn / critical)">
        {SEMANTIC_SWATCHES.map((s) => (
          <Swatch key={s.name} name={s.name} hex={s.hex} />
        ))}
      </Row>

      <div>
        <SubLabel>T1 dense-palette stress fixture (47-span graph, real layout + node components)</SubLabel>
        <div
          style={{
            height: 520,
            border: `1px solid ${surface.border}`,
            borderRadius: radii.xl,
            overflow: 'auto',
          }}
        >
          <DensePaletteHarness />
        </div>
      </div>
    </Section>
  )
}

// ---------------------------------------------------------------------------
// 3. Surface + elevation
// ---------------------------------------------------------------------------

const SURFACE_RUNGS = [
  { key: 'canvas', label: 'surface.canvas', background: surface.canvas, boxShadow: elevation.flat },
  { key: 'surface', label: 'surface.surface', background: surface.surface, boxShadow: elevation.flat },
  { key: 'raised', label: 'surface.raised', background: surface.raised, boxShadow: elevation.raised },
  { key: 'overlay', label: 'surface.overlay', background: surface.overlay, boxShadow: elevation.overlay },
]

function SurfaceElevationSection() {
  return (
    <Section
      index={3}
      title="Surface + elevation"
      note="A monotonic 4-tier ramp: canvas (page) -> surface (resting panel) -> raised (hover/active/selected) -> overlay (drawers/modals/menus). Each tier nests inside the one before it and carries its matching elevation shadow."
    >
      <div
        style={{
          background: SURFACE_RUNGS[0].background,
          border: `1px solid ${surface.border}`,
          borderRadius: radii.xxl,
          padding: spacing.xl,
        }}
      >
        <SubLabel>{SURFACE_RUNGS[0].label} (elevation.flat)</SubLabel>
        <div
          style={{
            background: SURFACE_RUNGS[1].background,
            border: `1px solid ${surface.border}`,
            borderRadius: radii.xl,
            boxShadow: SURFACE_RUNGS[1].boxShadow,
            padding: spacing.xl,
          }}
        >
          <SubLabel>{SURFACE_RUNGS[1].label} (elevation.flat)</SubLabel>
          <div
            style={{
              background: SURFACE_RUNGS[2].background,
              border: `1px solid ${surface.border}`,
              borderRadius: radii.lg,
              boxShadow: SURFACE_RUNGS[2].boxShadow,
              padding: spacing.xl,
            }}
          >
            <SubLabel>{SURFACE_RUNGS[2].label} (elevation.raised)</SubLabel>
            <div
              style={{
                background: SURFACE_RUNGS[3].background,
                border: `1px solid ${surface.border}`,
                borderRadius: radii.lg,
                boxShadow: SURFACE_RUNGS[3].boxShadow,
                padding: spacing.xl,
              }}
            >
              <SubLabel>{SURFACE_RUNGS[3].label} (elevation.overlay)</SubLabel>
              <p style={{ margin: 0, color: colors.subtext0, fontFamily: typeScale.body.fontFamily, fontSize: typeScale.body.fontSize }}>
                Top of the ramp — drawer/menu/modal-adjacent surfaces.
              </p>
            </div>
          </div>
        </div>
      </div>
    </Section>
  )
}

// ---------------------------------------------------------------------------
// 4. Primitives
// ---------------------------------------------------------------------------

const ALL_STATUSES = ['Pending', 'Running', 'WaitingForApproval', 'Completed', 'Failed', 'Skipped']

// Minimal decorative inbox glyph for the EmptyState icon demo — inline SVG,
// no external asset, aria-hidden by EmptyState itself.
function InboxIcon() {
  return (
    <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
      <path d="M3 8.5 6 4h12l3 4.5" strokeLinecap="round" strokeLinejoin="round" />
      <path
        d="M3 8.5h5.5a1 1 0 0 1 .9.55l.7 1.4a1 1 0 0 0 .9.55h2a1 1 0 0 0 .9-.55l.7-1.4a1 1 0 0 1 .9-.55H21V18a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V8.5Z"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

function ButtonRow() {
  return (
    <Row label="Button — primary + ghost, default + disabled (hover/active verified live)">
      <div style={{ width: 160 }}>
        <Button variant="primary">Primary</Button>
      </div>
      <div style={{ width: 160 }}>
        <Button variant="primary" disabled>
          Primary (disabled)
        </Button>
      </div>
      <div style={{ width: 160 }}>
        <Button variant="ghost">Ghost</Button>
      </div>
      <div style={{ width: 160 }}>
        <Button variant="ghost" disabled>
          Ghost (disabled)
        </Button>
      </div>
    </Row>
  )
}

function InputRow() {
  return (
    <Row label="Input — default, with label, disabled">
      <div style={{ width: 220 }}>
        <Input placeholder="Default input" value="" onChange={() => {}} />
      </div>
      <div style={{ width: 220 }}>
        <Input label="Task name" placeholder="With label" value="" onChange={() => {}} />
      </div>
      <div style={{ width: 220 }}>
        <Input label="Locked field" value="Read-only value" disabled onChange={() => {}} />
      </div>
    </Row>
  )
}

function TextAreaRow() {
  return (
    <Row label="TextArea — default, with label, disabled">
      <div style={{ width: 260 }}>
        <TextArea placeholder="Default textarea" rows={3} value="" onChange={() => {}} />
      </div>
      <div style={{ width: 260 }}>
        <TextArea label="Rejection note" placeholder="With label" rows={3} value="" onChange={() => {}} />
      </div>
      <div style={{ width: 260 }}>
        <TextArea label="Locked note" value="Read-only note text" rows={3} disabled onChange={() => {}} />
      </div>
    </Row>
  )
}

function LabelRow() {
  return (
    <Row label="Label">
      <Label>Task title</Label>
    </Row>
  )
}

function StateTextRow() {
  return (
    <Row label="StateText — tone muted + error">
      <StateText tone="muted">Loading…</StateText>
      <StateText tone="error">Failed to save changes.</StateText>
    </Row>
  )
}

function StatusBadgeRow() {
  return (
    <Row label="StatusBadge — all 6 statuses">
      {ALL_STATUSES.map((status) => (
        <StatusBadge key={status} status={status} />
      ))}
    </Row>
  )
}

function NavRow() {
  return (
    <Row label="NavItem — active + inactive, inside Nav">
      <Nav>
        <NavItem active>Playground</NavItem>
        <NavItem>Memories</NavItem>
        <NavItem>Observability</NavItem>
        <NavItem>Evals</NavItem>
      </Nav>
    </Row>
  )
}

function PanelRow() {
  return (
    <Row label="Panel — default + one per accentStatus">
      <div style={{ width: 200 }}>
        <Panel>
          <Label>Default</Label>
          <p style={{ margin: `${spacing.xs} 0 0`, color: colors.text, fontFamily: typeScale.body.fontFamily, fontSize: 13 }}>
            No accent
          </p>
        </Panel>
      </div>
      {ALL_STATUSES.map((status) => (
        <div key={status} style={{ width: 200 }}>
          <Panel accentStatus={status}>
            <Label>{status}</Label>
            <p style={{ margin: `${spacing.xs} 0 0`, color: colors.text, fontFamily: typeScale.body.fontFamily, fontSize: 13 }}>
              accentStatus=&quot;{status}&quot;
            </p>
          </Panel>
        </div>
      ))}
    </Row>
  )
}

function EmptyStateRow() {
  return (
    <Row label="EmptyState — title-only, title+description, title+description+action+icon">
      <div style={{ width: 260 }}>
        <Card level="surface" padding="0">
          <EmptyState title="No memories yet" />
        </Card>
      </div>
      <div style={{ width: 260 }}>
        <Card level="surface" padding="0">
          <EmptyState title="No traces yet" description="Run a task to see its execution timeline here." />
        </Card>
      </div>
      <div style={{ width: 260 }}>
        <Card level="surface" padding="0">
          <EmptyState
            icon={<InboxIcon />}
            title="No approvals pending"
            description="Approvals raised by an agent will show up here for review."
            action={<Button variant="primary">Refresh</Button>}
          />
        </Card>
      </div>
    </Row>
  )
}

function ToggleDemo({ ariaLabel, options }) {
  const [value, setValue] = useState(options[0].value)
  return <Toggle aria-label={ariaLabel} options={options} value={value} onChange={setValue} />
}

function ToggleRow() {
  return (
    <Row label="Toggle — live 2-segment + 3-segment (click / arrow keys to navigate)">
      <ToggleDemo
        ariaLabel="View mode"
        options={[
          { value: 'list', label: 'List' },
          { value: 'graph', label: 'Graph' },
        ]}
      />
      <ToggleDemo
        ariaLabel="Time range"
        options={[
          { value: '1h', label: '1h' },
          { value: '24h', label: '24h' },
          { value: '7d', label: '7d' },
        ]}
      />
    </Row>
  )
}

function CardRow() {
  return (
    <Row label="Card — one per level (surface / raised / overlay)">
      <div style={{ width: 220 }}>
        <Card level="surface">
          <Label>surface</Label>
          <p style={{ margin: `${spacing.xs} 0 0`, color: colors.text, fontFamily: typeScale.body.fontFamily, fontSize: 13 }}>
            Resting-tier content.
          </p>
        </Card>
      </div>
      <div style={{ width: 220 }}>
        <Card level="raised">
          <Label>raised</Label>
          <p style={{ margin: `${spacing.xs} 0 0`, color: colors.text, fontFamily: typeScale.body.fontFamily, fontSize: 13 }}>
            Hover/active-adjacent content.
          </p>
        </Card>
      </div>
      <div style={{ width: 220 }}>
        <Card level="overlay">
          <Label>overlay</Label>
          <p style={{ margin: `${spacing.xs} 0 0`, color: colors.text, fontFamily: typeScale.body.fontFamily, fontSize: 13 }}>
            Drawer/menu-adjacent content.
          </p>
        </Card>
      </div>
    </Row>
  )
}

function PrimitivesSection() {
  return (
    <Section index={4} title="Primitives x states">
      <ButtonRow />
      <InputRow />
      <TextAreaRow />
      <LabelRow />
      <StateTextRow />
      <StatusBadgeRow />
      <NavRow />
      <PanelRow />
      <EmptyStateRow />
      <ToggleRow />
      <CardRow />
    </Section>
  )
}

// ---------------------------------------------------------------------------
// 5. Two-Face content (P2-M2 Task A1) — verification specimen
//
// Side-by-side proof of the canonical element -> face mapping (DESIGN.md §3
// "Canonical element -> face mapping"): UI-face content (heading, nav label,
// Button, instruction prose, status message) rendered via typeScale's UI
// steps, next to data-face content (ID, timestamp, duration, cost, code/
// JSON, a span-ID table row) rendered via typeScale's data* steps. Every
// font-family below is read from typeScale/tokens — nothing hardcoded.
// ---------------------------------------------------------------------------

function TwoFaceColumn({ title, children }) {
  return (
    <Card level="surface">
      <div
        style={{
          fontFamily: typeScale.label.fontFamily,
          fontSize: typeScale.label.fontSize,
          fontWeight: typeScale.label.fontWeight,
          letterSpacing: typeScale.label.letterSpacing,
          textTransform: 'uppercase',
          color: colors.overlay0,
          marginBottom: spacing.md,
        }}
      >
        {title}
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: spacing.md, alignItems: 'flex-start' }}>
        {children}
      </div>
    </Card>
  )
}

function UiFaceSpecimen() {
  return (
    <TwoFaceColumn title="UI face — IBM Plex Sans">
      <h2
        style={{
          margin: 0,
          color: colors.text,
          fontFamily: typeScale.heading.fontFamily,
          fontSize: typeScale.heading.fontSize,
          fontWeight: typeScale.heading.fontWeight,
          lineHeight: typeScale.heading.lineHeight,
        }}
      >
        Trace Overview
      </h2>
      {/* NavItem/Button/StateText don't carry the UI face yet (they still
          inherit #root's mono default until Task A2 migrates them) — the
          inline fontFamily overrides below are verification-only, so this
          panel demonstrates the intended A2 end-state, not the pre-A2
          rendering. They do not touch the real components. */}
      <Nav>
        <NavItem active style={{ fontFamily: uiFontFamily }}>
          Observability
        </NavItem>
      </Nav>
      <div style={{ width: 160 }}>
        <Button variant="primary" style={{ fontFamily: uiFontFamily }}>
          Run task
        </Button>
      </div>
      <p
        style={{
          margin: 0,
          color: colors.subtext0,
          fontFamily: typeScale.body.fontFamily,
          fontSize: typeScale.body.fontSize,
          lineHeight: typeScale.body.lineHeight,
          maxWidth: 320,
        }}
      >
        Select an agent from the sidebar to inspect its execution trace and step-by-step reasoning.
      </p>
      <StateText tone="muted" style={{ fontFamily: uiFontFamily }}>
        All systems operational.
      </StateText>
    </TwoFaceColumn>
  )
}

const TWO_FACE_SPANS = [
  { id: 'span_88f3a1', duration: '812 ms', cost: '$0.0091' },
  { id: 'span_c02e77', duration: '204 ms', cost: '$0.0018' },
]

function DataFaceSpecimen() {
  return (
    <TwoFaceColumn title="Data face — JetBrains Mono">
      <div
        style={{
          fontFamily: typeScale.dataLabel.fontFamily,
          fontSize: typeScale.dataLabel.fontSize,
          fontWeight: typeScale.dataLabel.fontWeight,
          color: colors.subtext0,
        }}
      >
        task_9f2a1c7e
      </div>
      <div
        style={{
          fontFamily: typeScale.dataBody.fontFamily,
          fontSize: typeScale.dataBody.fontSize,
          fontWeight: typeScale.dataBody.fontWeight,
          color: colors.text,
        }}
      >
        2026-07-26T14:32:07Z
      </div>
      <div style={{ display: 'flex', gap: spacing.lg }}>
        <div
          style={{
            fontFamily: typeScale.dataValue.fontFamily,
            fontSize: typeScale.dataValue.fontSize,
            fontWeight: typeScale.dataValue.fontWeight,
            color: colors.text,
          }}
        >
          812 ms
        </div>
        <div
          style={{
            fontFamily: typeScale.dataValue.fontFamily,
            fontSize: typeScale.dataValue.fontSize,
            fontWeight: typeScale.dataValue.fontWeight,
            color: colors.signalGreen,
          }}
        >
          $0.0091
        </div>
      </div>
      <pre
        style={{
          margin: 0,
          padding: spacing.sm,
          background: surface.raised,
          borderRadius: radii.md,
          fontFamily: typeScale.dataBody.fontFamily,
          fontSize: typeScale.dataCaption.fontSize,
          color: colors.text,
          overflowX: 'auto',
          maxWidth: 320,
        }}
      >
        {'{ "status": "completed", "spanId": "span_88f3a1" }'}
      </pre>
      <table
        style={{
          width: '100%',
          borderCollapse: 'collapse',
          fontFamily: typeScale.dataBody.fontFamily,
          fontSize: typeScale.dataCaption.fontSize,
          color: colors.text,
        }}
      >
        <thead>
          <tr>
            <th style={{ textAlign: 'left', padding: spacing.xs, borderBottom: `1px solid ${surface.border}` }}>
              Span ID
            </th>
            <th style={{ textAlign: 'left', padding: spacing.xs, borderBottom: `1px solid ${surface.border}` }}>
              Duration
            </th>
            <th style={{ textAlign: 'left', padding: spacing.xs, borderBottom: `1px solid ${surface.border}` }}>
              Cost
            </th>
          </tr>
        </thead>
        <tbody>
          {TWO_FACE_SPANS.map((s) => (
            <tr key={s.id}>
              <td style={{ padding: spacing.xs }}>{s.id}</td>
              <td style={{ padding: spacing.xs }}>{s.duration}</td>
              <td style={{ padding: spacing.xs }}>{s.cost}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </TwoFaceColumn>
  )
}

function TwoFaceSection() {
  return (
    <Section
      index={5}
      title="Two-Face content"
      note="P2-M2 Task A1 verification specimen: UI-face content (heading, nav, button, prose, status) in IBM Plex Sans side by side with data-face content (ID, timestamp, duration, cost, code/JSON, span-ID table row) in JetBrains Mono — proving the canonical element -> face mapping renders as documented."
    >
      <div style={{ display: 'flex', gap: spacing.xl, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <div style={{ flex: '1 1 320px', minWidth: 320 }}>
          <UiFaceSpecimen />
        </div>
        <div style={{ flex: '1 1 320px', minWidth: 320 }}>
          <DataFaceSpecimen />
        </div>
      </div>
    </Section>
  )
}

// ---------------------------------------------------------------------------
// Root
// ---------------------------------------------------------------------------

export default function ComponentGallery() {
  return (
    <div
      style={{
        minHeight: '100vh',
        background: surface.canvas,
        color: colors.text,
        padding: `${spacing.xxl} ${spacing.xl} ${spacing.xxxl}`,
        boxSizing: 'border-box',
      }}
    >
      <header style={{ marginBottom: spacing.xxxl }}>
        <h1
          style={{
            margin: 0,
            color: colors.text,
            fontFamily: uiFontFamily,
            fontSize: 32,
            fontWeight: 700,
          }}
        >
          OrchestAI — Design System Gallery
        </h1>
        <p
          style={{
            margin: `${spacing.xs} 0 0`,
            color: colors.subtext0,
            fontFamily: typeScale.body.fontFamily,
            fontSize: typeScale.body.fontSize,
            maxWidth: 760,
          }}
        >
          R1 (Design-System Foundation) — verification-only surface for the Go/No-Go A review. Every value below is
          read live from theme/tokens.js, theme/typography.js, and the shared component modules; nothing is
          hand-copied.
        </p>
      </header>

      <TypographySection />
      <PaletteSection />
      <SurfaceElevationSection />
      <PrimitivesSection />
      <TwoFaceSection />
    </div>
  )
}
