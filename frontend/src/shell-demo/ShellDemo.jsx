// frontend/src/shell-demo/ShellDemo.jsx
//
// P2-M1A Task 5 — the Shell Demo: assembles the REAL AppShell + Sidebar +
// TopBar + PageContainer (T2-T4) with representative SYNTHETIC content, for
// live 6-viewport visual review (the Go/No-Go artifact). This is NOT a
// product page: no product route/page/App.jsx is imported or touched, and
// there are no backend calls anywhere below — every "run", "cost", "span
// id" etc. is a hardcoded/deterministically-generated fixture. Every
// color/spacing/radius/type value comes from theme/tokens.js or an R1
// primitive that already consumes it; nothing here hand-rolls a hex/px
// value or reimplements a primitive.
//
// ---------------------------------------------------------------------------
// View catalogue (local `view` state, switched by clicking a Sidebar item —
// demonstrates active-nav + navigation together)
// ---------------------------------------------------------------------------
//   dashboard — width="full"     — bento grid of Cards (stat tiles +
//                                   a recent-activity list), ultra-wide use
//   dense     — width="wide"     — a synthetic agent-run table (Panel +
//                                   mono/data cells, StatusBadge per row)
//                                   with a Toggle segmented filter above it
//   readable  — width="readable" — prose + a form (Input/TextArea/Button/
//                                   Label) at the constrained reading measure
//   empty     — width="standard" — EmptyState (icon + title + description +
//                                   action Button)
//   long      — width="standard" — many stacked Panels, scrolling well past
//                                   one viewport — proves the sticky TopBar
//                                   stays put and nothing scrolls
//                                   horizontally
//
// The Sidebar's own collapse toggle, the mobile drawer (via TopBar's menu
// button), hover/focus on every control, and the populated TopBar context
// cluster (synthetic task id in the mono/data face, a StatusBadge, a cost
// figure) are all reachable simply by running this demo at different
// viewports/interactions — nothing below scripts them; the controller
// drives them live (T6/T7).

import { useState } from 'react'
import { AppShell } from '../components/shell/AppShell'
import { Sidebar } from '../components/shell/Sidebar'
import { TopBar } from '../components/shell/TopBar'
import { PageContainer } from '../components/shell/PageContainer'
import { Panel } from '../components/Panel'
import { Card } from '../components/Card'
import { Button } from '../components/Button'
import { Input, TextArea } from '../components/Input'
import { Label } from '../components/Label'
import { StatusBadge } from '../components/StatusBadge'
import { EmptyState } from '../components/EmptyState'
import { Toggle } from '../components/Toggle'
import { StateText } from '../components/StateText'
import { colors, surface, spacing, typography, typeScale } from '../theme/tokens'

// ---------------------------------------------------------------------------
// Inline nav/brand icons — monochrome, stroke-based, `currentColor` (the
// dev-tool aesthetic the brief requires; no emoji anywhere in this file).
// Mirrors the existing inline-icon pattern already shipped in
// shell/Sidebar.jsx (`CollapseChevron`) and shell/TopBar.jsx (`MenuIcon`):
// small, unexported, local to the file that uses them. `NavItem` (via
// `Sidebar`) already wraps whatever it's given in an `aria-hidden` span, and
// `EmptyState` does the same for its own `icon` prop — so none of these set
// `aria-hidden` themselves, matching how `Sidebar`'s existing icon consumers
// behave.
// ---------------------------------------------------------------------------

function DashboardIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <rect x="3" y="3" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <rect x="11" y="3" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <rect x="3" y="11" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
      <rect x="11" y="11" width="6" height="6" rx="1" stroke="currentColor" strokeWidth="1.5" />
    </svg>
  )
}

function TableIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <rect x="2.5" y="3.5" width="15" height="13" rx="1.5" stroke="currentColor" strokeWidth="1.5" />
      <path d="M2.5 8h15M2.5 12.5h15M8 3.5v13" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  )
}

function DocumentIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <path
        d="M5 2.5h7l3 3v12a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-14a1 1 0 0 1 1-1Z"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
      <path d="M7 9h6M7 12.5h6M7 16h3.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  )
}

function InboxIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <path
        d="M3 11.5 5.5 4h9L17 11.5v3a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 3 14.5v-3Z"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
      <path
        d="M3 11.5h4.2a1 1 0 0 1 .9.55l.4.8a1 1 0 0 0 .9.55h1.2a1 1 0 0 0 .9-.55l.4-.8a1 1 0 0 1 .9-.55H17"
        stroke="currentColor"
        strokeWidth="1.5"
        strokeLinejoin="round"
      />
    </svg>
  )
}

function ListIcon() {
  return (
    <svg width="18" height="18" viewBox="0 0 20 20" fill="none">
      <circle cx="4" cy="5" r="1" fill="currentColor" />
      <circle cx="4" cy="10" r="1" fill="currentColor" />
      <circle cx="4" cy="15" r="1" fill="currentColor" />
      <path d="M7.5 5h9M7.5 10h9M7.5 15h9" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  )
}

// Decorative brand mark for the sidebar header — a plain monochrome glyph
// (not a product logo), hidden entirely by Sidebar while collapsed per its
// own documented `brand` contract.
function BrandMark() {
  return (
    <svg width="22" height="22" viewBox="0 0 20 20" fill="none" style={{ flexShrink: 0 }}>
      <path d="M10 2 17.5 6v8L10 18 2.5 14V6L10 2Z" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" />
      <path
        d="M10 2v16M2.5 6l7.5 4 7.5-4M2.5 14l7.5-4 7.5 4"
        stroke="currentColor"
        strokeWidth="1.2"
        strokeLinejoin="round"
      />
    </svg>
  )
}

const NAV_ITEMS = [
  { key: 'dashboard', label: 'Dashboard', icon: <DashboardIcon /> },
  { key: 'dense', label: 'Dense Table', icon: <TableIcon /> },
  { key: 'readable', label: 'Readable', icon: <DocumentIcon /> },
  { key: 'empty', label: 'Empty', icon: <InboxIcon /> },
  { key: 'long', label: 'Long', icon: <ListIcon /> },
]

const VIEW_TITLES = {
  dashboard: 'Dashboard',
  dense: 'Agent Runs',
  readable: 'Task Brief',
  empty: 'Recent Runs',
  long: 'Execution Log',
}

// ---------------------------------------------------------------------------
// Synthetic fixture data — deterministic (no Math.random/Date.now), so the
// demo renders identically on every load/module-eval. Representative of
// agent-run/span data, not a copy of any real page's shape.
// ---------------------------------------------------------------------------

const RUN_AGENTS = ['OrchestratorAgent', 'ResearchAgent', 'CodeAgent', 'DataAgent']
const RUN_STATUSES = ['Running', 'Completed', 'Completed', 'Failed', 'Pending', 'WaitingForApproval', 'Skipped']
const RUN_MODELS = ['claude-sonnet-4-6', 'claude-haiku-4-6']

function buildDemoRuns(count) {
  return Array.from({ length: count }, (_, i) => {
    const tokensIn = 1800 + i * 63
    const tokensOut = 220 + i * 19
    return {
      id: `span_${(0x1a2b3c + i * 97).toString(16)}`,
      agent: RUN_AGENTS[i % RUN_AGENTS.length],
      status: RUN_STATUSES[i % RUN_STATUSES.length],
      model: RUN_MODELS[i % RUN_MODELS.length],
      durationMs: 420 + i * 137,
      tokensIn,
      tokensOut,
      costUsd: (0.0006 * tokensIn + 0.0024 * tokensOut) / 100,
      startedAt: `2026-07-26T14:${String(2 + (i % 55)).padStart(2, '0')}:00Z`,
    }
  })
}

const DEMO_RUNS = buildDemoRuns(24)

const LOG_SECTIONS = Array.from({ length: 24 }, (_, i) => ({
  id: i,
  title: `Step ${i + 1} — ${RUN_AGENTS[i % RUN_AGENTS.length]}`,
  body:
    `Synthetic log entry ${i + 1} of ${24}. This section exists purely to prove the shell's ` +
    'sticky top bar stays fixed and no horizontal scrollbar appears while scrolling a long ' +
    'content region — it is not real agent output.',
}))

const cellStyle = {
  padding: spacing.sm,
  borderBottom: `1px solid ${surface.border}`,
  whiteSpace: 'nowrap',
  ...typography.body,
  color: colors.text,
}

const headerCellStyle = {
  textAlign: 'left',
  padding: spacing.sm,
  borderBottom: `1px solid ${surface.border}`,
  whiteSpace: 'nowrap',
  ...typography.label,
  color: colors.overlay0,
}

const TABLE_COLUMNS = ['Span ID', 'Agent', 'Status', 'Model', 'Duration', 'Tokens In', 'Tokens Out', 'Cost', 'Started']

const FILTER_OPTIONS = [
  { value: 'all', label: 'All' },
  { value: 'running', label: 'Running' },
  { value: 'failed', label: 'Failed' },
]

// ---------------------------------------------------------------------------
// Views
// ---------------------------------------------------------------------------

const STAT_TILES = [
  { label: 'Active Agents', value: '6', hint: '+2 vs last hour', level: 'surface' },
  { label: 'Tasks Today', value: '128', hint: '94.2% success', level: 'surface' },
  { label: 'Avg Cost / Task', value: '$0.0091', hint: 'claude-sonnet-4-6', level: 'raised' },
  { label: 'Tokens Processed', value: '2.4M', hint: 'in + out', level: 'surface' },
  { label: 'P95 Latency', value: '812 ms', hint: 'span-to-span', level: 'surface' },
  { label: 'Open Approvals', value: '3', hint: 'awaiting review', level: 'raised' },
]

function DashboardView() {
  return (
    <PageContainer width="full">
      <div style={{ padding: `${spacing.xl} ${spacing.xl}`, display: 'flex', flexDirection: 'column', gap: spacing.xl }}>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))', gap: spacing.lg }}>
          {STAT_TILES.map(tile => (
            <Card key={tile.label} level={tile.level}>
              <Label>{tile.label}</Label>
              <div style={{ marginTop: spacing.xs, ...typeScale.dataValue, color: colors.text }}>{tile.value}</div>
              <StateText style={{ marginTop: spacing.xs }}>{tile.hint}</StateText>
            </Card>
          ))}
        </div>

        <Card>
          <Label>Recent Activity</Label>
          <div style={{ marginTop: spacing.md, display: 'flex', flexDirection: 'column', gap: spacing.sm }}>
            {DEMO_RUNS.slice(0, 5).map(run => (
              <div
                key={run.id}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: spacing.md,
                  flexWrap: 'wrap',
                  justifyContent: 'space-between',
                }}
              >
                <span style={{ ...typography.body, color: colors.text }}>{run.agent}</span>
                <span style={{ ...typography.body, color: colors.subtext0 }}>{run.id}</span>
                <StatusBadge status={run.status} />
              </div>
            ))}
          </div>
        </Card>
      </div>
    </PageContainer>
  )
}

function DenseTableView() {
  const [filter, setFilter] = useState('all')
  const rows = DEMO_RUNS.filter(run => {
    if (filter === 'running') return run.status === 'Running'
    if (filter === 'failed') return run.status === 'Failed'
    return true
  })

  return (
    <PageContainer width="wide">
      <div style={{ padding: `${spacing.xl} 0`, display: 'flex', flexDirection: 'column', gap: spacing.lg }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            flexWrap: 'wrap',
            gap: spacing.md,
          }}
        >
          <div>
            <h2 style={{ margin: 0, ...typeScale.heading, color: colors.text }}>Agent Runs</h2>
            <p style={{ margin: `${spacing.xs} 0 0`, ...typography.body, color: colors.subtext0 }}>
              {rows.length} of {DEMO_RUNS.length} runs — a dense, data-heavy view for wide viewports.
            </p>
          </div>
          <Toggle aria-label="Filter agent runs" options={FILTER_OPTIONS} value={filter} onChange={setFilter} />
        </div>

        <Panel style={{ padding: 0 }}>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 960 }}>
              <thead>
                <tr>
                  {TABLE_COLUMNS.map(col => (
                    <th key={col} style={headerCellStyle}>
                      {col}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map(run => (
                  <tr key={run.id}>
                    <td style={cellStyle}>{run.id}</td>
                    <td style={cellStyle}>{run.agent}</td>
                    <td style={cellStyle}>
                      <StatusBadge status={run.status} />
                    </td>
                    <td style={cellStyle}>{run.model}</td>
                    <td style={cellStyle}>{run.durationMs} ms</td>
                    <td style={cellStyle}>{run.tokensIn.toLocaleString()}</td>
                    <td style={cellStyle}>{run.tokensOut.toLocaleString()}</td>
                    <td style={cellStyle}>${run.costUsd.toFixed(4)}</td>
                    <td style={cellStyle}>{run.startedAt}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      </div>
    </PageContainer>
  )
}

function ReadableView() {
  const [instruction, setInstruction] = useState('')
  const [notes, setNotes] = useState('')

  return (
    <PageContainer width="readable">
      <div style={{ padding: `${spacing.xl} 0`, display: 'flex', flexDirection: 'column', gap: spacing.lg }}>
        <h2 style={{ margin: 0, ...typeScale.heading, color: colors.text }}>Task Brief</h2>
        <p style={{ margin: 0, ...typography.body, color: colors.subtext0 }}>
          This view demonstrates the constrained reading measure PageContainer applies with{' '}
          <code>width=&quot;readable&quot;</code> (68ch) — the same soft cap a task-brief, documentation, or
          single-column prose surface uses once a product page migrates onto this shell.
        </p>
        <p style={{ margin: 0, ...typography.body, color: colors.subtext0 }}>
          Below it, a short form exercises the shared field primitives — a label directly above each
          field, the shared focus ring, and both button variants — at the width they will actually be
          read and used at, not stretched edge-to-edge.
        </p>

        <Panel>
          <div style={{ display: 'flex', flexDirection: 'column', gap: spacing.md }}>
            <Input
              label="Task instruction"
              value={instruction}
              onChange={event => setInstruction(event.target.value)}
              placeholder="Summarize the last 20 failed runs and propose a fix..."
            />
            <TextArea
              label="Context notes"
              value={notes}
              onChange={event => setNotes(event.target.value)}
              rows={4}
              placeholder="Optional context for the orchestrator..."
            />
            <div style={{ display: 'flex', gap: spacing.sm }}>
              <div style={{ width: 140 }}>
                <Button variant="primary">Run task</Button>
              </div>
              <div style={{ width: 120 }}>
                <Button
                  variant="ghost"
                  onClick={() => {
                    setInstruction('')
                    setNotes('')
                  }}
                >
                  Clear
                </Button>
              </div>
            </div>
          </div>
        </Panel>
      </div>
    </PageContainer>
  )
}

function EmptyView() {
  return (
    <PageContainer width="standard">
      <div style={{ padding: `${spacing.xxxl} 0`, display: 'flex', justifyContent: 'center' }}>
        <Card style={{ maxWidth: 480, width: '100%' }}>
          <EmptyState
            icon={<InboxIcon />}
            title="No agent runs yet"
            description="Runs started from the Playground or API will appear here once they begin executing."
            action={
              <div style={{ width: 200 }}>
                <Button variant="primary">Start a run</Button>
              </div>
            }
          />
        </Card>
      </div>
    </PageContainer>
  )
}

function LongView() {
  return (
    <PageContainer width="standard">
      <div style={{ padding: `${spacing.xl} 0`, display: 'flex', flexDirection: 'column', gap: spacing.xl }}>
        <p style={{ margin: 0, ...typography.body, color: colors.subtext0 }}>
          Scroll this view — the top bar should stay put and the page should never scroll
          horizontally, even as the rows below grow the total content height well past a single
          viewport.
        </p>
        {LOG_SECTIONS.map(section => (
          <Panel key={section.id}>
            <h3 style={{ margin: 0, ...typography.title, color: colors.text }}>{section.title}</h3>
            <p style={{ margin: `${spacing.sm} 0 0`, ...typography.body, color: colors.subtext0 }}>{section.body}</p>
            <div style={{ marginTop: spacing.sm, display: 'flex', gap: spacing.sm, flexWrap: 'wrap' }}>
              <StatusBadge status={RUN_STATUSES[section.id % RUN_STATUSES.length]} />
              <span style={{ ...typography.body, color: colors.overlay0 }}>
                span_{(0x4f2a + section.id).toString(16)}
              </span>
            </div>
          </Panel>
        ))}
      </div>
    </PageContainer>
  )
}

const VIEW_RENDERERS = {
  dashboard: DashboardView,
  dense: DenseTableView,
  readable: ReadableView,
  empty: EmptyView,
  long: LongView,
}

/**
 * The Shell Demo — mounts the REAL <AppShell> with a real <Sidebar>/<TopBar>
 * and synthetic, representative content, so the shell can be validated live
 * at every reference viewport. See the file header for the view catalogue.
 */
export default function ShellDemo() {
  const [view, setView] = useState('dashboard')
  const ActiveView = VIEW_RENDERERS[view] ?? DashboardView

  return (
    <AppShell
      sidebar={
        <Sidebar
          items={NAV_ITEMS}
          active={view}
          onNavigate={setView}
          brand={
            <div style={{ display: 'flex', alignItems: 'center', gap: spacing.sm, color: colors.text }}>
              <BrandMark />
              <div>
                <div style={{ ...typeScale.subheading, color: colors.text }}>OrchestAI</div>
                <div style={{ ...typeScale.caption, color: colors.subtext0 }}>Shell Prototype</div>
              </div>
            </div>
          }
        />
      }
      topBar={
        <TopBar title={VIEW_TITLES[view]}>
          <span style={{ ...typeScale.dataLabel, color: colors.subtext0 }}>task_9f2ae7c1b3d4</span>
          <StatusBadge status="Running" />
          <span style={{ ...typeScale.dataLabel, color: colors.text }}>$0.0842</span>
        </TopBar>
      }
    >
      <ActiveView />
    </AppShell>
  )
}
