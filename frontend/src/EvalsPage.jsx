// frontend/src/EvalsPage.jsx
import { useState, useEffect } from 'react'
import { authenticatedFetch } from './apiKey'
import { colors, radii, uiFontFamily, dataFontFamily, spacing } from './theme/tokens'
import { Panel } from './components/Panel'
import { Label } from './components/Label'
import { Input, TextArea } from './components/Input'
import { Button } from './components/Button'
import { Nav, NavItem } from './components/NavItem'
import { StateText } from './components/StateText'
import { Spinner } from './components/Spinner'
import { Toggle } from './components/Toggle'
import { EmptyState } from './components/EmptyState'
import { ErrorBanner } from './components/ErrorBanner'

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`

// <select>/native date & number inputs aren't a good fit for the Input component
// (whose fieldStyle padding/fontSize differ from this file's original look) —
// styled directly from tokens.js values instead. Matches the original selectStyle
// shape exactly (padding, borderRadius, border, background, color, fontSize,
// outline), just with hardcoded hex replaced by token references.
const selectStyle = {
  padding: '6px 10px',
  borderRadius: radii.lg,
  border: `1px solid ${colors.surface0}`,
  background: colors.mantle,
  color: colors.text,
  fontSize: 12,
  fontFamily: dataFontFamily,
  outline: 'none',
}

function SubNav({ subView, setSubView }) {
  const tabs = [['suites', 'Suites'], ['run', 'Run'], ['results', 'Results'], ['posthoc', 'Post-Hoc']]
  return (
    <div style={{ marginBottom: 20, borderBottom: `1px solid ${colors.base}`, paddingBottom: 12 }}>
      <Nav>
        {tabs.map(([key, label]) => (
          <NavItem
            key={key}
            active={subView === key}
            onClick={() => setSubView(key)}
            style={{ padding: '6px 14px', fontWeight: subView === key ? 700 : 400 }}
          >
            {label}
          </NavItem>
        ))}
      </Nav>
    </div>
  )
}

function SuitesView({ suites, selectedSuiteId, onSelect }) {
  return (
    <Panel>
      <div style={{ marginBottom: 8 }}>
        <Label style={{ fontWeight: 400 }}>Eval Suites</Label>
      </div>
      {suites == null ? (
        <Spinner label="Loading suites…" />
      ) : (
        <>
          {suites.length === 0 && (
            <EmptyState
              title="No eval suites yet"
              description={
                <>
                  Eval suites are provisioned out-of-band — create one via{' '}
                  <code>POST /api/v1/eval-suites</code>.
                </>
              }
            />
          )}
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            {suites.map(s => (
              <div
                key={s.id}
                onClick={() => onSelect(s.id)}
                style={{
                  padding: '10px 12px', borderRadius: radii.lg, cursor: 'pointer',
                  background: selectedSuiteId === s.id ? colors.surface0 : colors.mantle,
                  border: selectedSuiteId === s.id ? `1px solid ${colors.traceBlue}` : `1px solid ${colors.surface0}`,
                }}
              >
                <div style={{ fontSize: 13, fontWeight: 700, color: colors.text, fontFamily: uiFontFamily }}>{s.name}</div>
                <div style={{ fontSize: 11, color: colors.overlay0, fontFamily: uiFontFamily }}>
                  {s.targetAgentType} · {s.description}
                </div>
              </div>
            ))}
          </div>
        </>
      )}
    </Panel>
  )
}

function RunView({ suites, selectedSuiteId, onRunTriggered }) {
  const [runs, setRuns] = useState([])
  const [subjectVersion, setSubjectVersion] = useState('')
  const [baselineRunId, setBaselineRunId] = useState('')
  const [error, setError] = useState(null)
  const suite = suites?.find(s => s.id === selectedSuiteId)

  useEffect(() => {
    if (!selectedSuiteId) return
    authenticatedFetch(`${API_BASE}/eval-suites/${selectedSuiteId}/runs`)
      .then(res => res.json())
      .then(data => setRuns(data.runs))
      .catch(() => setRuns([]))
  }, [selectedSuiteId])

  if (!selectedSuiteId) {
    return (
      <Panel>
        <EmptyState title="Select a suite first" style={{ padding: `${spacing.xl} ${spacing.lg}` }} />
      </Panel>
    )
  }

  const trigger = () => {
    setError(null)
    authenticatedFetch(`${API_BASE}/eval-suites/${selectedSuiteId}/runs`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        subjectVersion,
        baselineRunId: baselineRunId || null,
      }),
    })
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? `Failed: ${res.status}`)
        return res.json()
      })
      .then(data => onRunTriggered(data.evalRunId))
      .catch(err => setError(err.message))
  }

  return (
    <Panel>
      <div style={{ marginBottom: 8 }}>
        <Label style={{ fontWeight: 400 }}>Trigger a run — {suite?.name}</Label>
      </div>
      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <div>
          <div style={{ marginBottom: 8 }}>
            <Label style={{ fontWeight: 400 }}>Subject version</Label>
          </div>
          <Input
            value={subjectVersion}
            onChange={e => setSubjectVersion(e.target.value)}
            placeholder="git SHA or prompt version"
            style={{ padding: '6px 10px', fontSize: 12, outline: 'none', width: 220 }}
          />
        </div>
        <div>
          <div style={{ marginBottom: 8 }}>
            <Label style={{ fontWeight: 400 }}>Baseline run</Label>
          </div>
          <select value={baselineRunId} onChange={e => setBaselineRunId(e.target.value)} style={{ ...selectStyle, width: 260 }}>
            <option value="">None (first run for this suite)</option>
            {runs.map(r => (
              <option key={r.id} value={r.id}>
                {r.subjectVersion} — {r.status} — {new Date(r.triggeredAt).toLocaleString()}
              </option>
            ))}
          </select>
        </div>
        <Button
          variant="primary"
          onClick={trigger}
          disabled={!subjectVersion}
          style={{ padding: '6px 14px', width: 'auto', fontSize: 12, background: colors.traceBlue, color: colors.crust }}
        >
          Run suite
        </Button>
      </div>
      {error && <ErrorBanner style={{ marginTop: 10 }}>{error}</ErrorBanner>}
    </Panel>
  )
}

function ResultsView({ suites, selectedSuiteId, selectedRunId, onSelectRun }) {
  const [runs, setRuns] = useState([])
  const [results, setResults] = useState(null)
  const [resultsError, setResultsError] = useState(null)
  const [regression, setRegression] = useState(null)
  const [regressionError, setRegressionError] = useState(null)

  useEffect(() => {
    if (!selectedSuiteId) return
    authenticatedFetch(`${API_BASE}/eval-suites/${selectedSuiteId}/runs`)
      .then(res => res.json())
      .then(data => setRuns(data.runs))
      .catch(() => setRuns([]))
  }, [selectedSuiteId])

  useEffect(() => {
    if (!selectedRunId) return
    setResults(null)
    setResultsError(null)
    setRegression(null)
    setRegressionError(null)

    authenticatedFetch(`${API_BASE}/eval-runs/${selectedRunId}/results`)
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? `Failed: ${res.status}`)
        return res.json()
      })
      .then(setResults)
      .catch(err => setResultsError(err.message))

    authenticatedFetch(`${API_BASE}/eval-runs/${selectedRunId}/regression-report`)
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? 'No baseline set for this run')
        return res.json()
      })
      .then(setRegression)
      .catch(err => setRegressionError(err.message))
  }, [selectedRunId])

  const passCount = results ? results.results.filter(r => r.passed).length : 0

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <Panel>
        <div style={{ marginBottom: 8 }}>
          <Label style={{ fontWeight: 400 }}>Run</Label>
        </div>
        <select value={selectedRunId ?? ''} onChange={e => onSelectRun(e.target.value)} style={{ ...selectStyle, width: '100%' }}>
          <option value="" disabled>Select a run…</option>
          {runs.map(r => (
            <option key={r.id} value={r.id}>
              {r.subjectVersion} — {r.status} — {new Date(r.triggeredAt).toLocaleString()}
            </option>
          ))}
        </select>
      </Panel>

      {resultsError && <ErrorBanner>{resultsError}</ErrorBanner>}

      {selectedRunId && !results && !resultsError && (
        <Panel><Spinner label="Loading results…" /></Panel>
      )}

      {results && (
        <Panel>
          <div style={{ marginBottom: 8 }}>
            <Label style={{ fontWeight: 400 }}>Pass/Fail Summary</Label>
          </div>
          <div style={{ fontSize: 20, fontWeight: 700, color: colors.text, fontFamily: uiFontFamily }}>
            <span style={{ fontFamily: dataFontFamily }}>{passCount} / {results.results.length}</span> passed
          </div>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12, fontFamily: uiFontFamily }}>
              <thead>
                <tr style={{ color: colors.overlay0, textAlign: 'left' }}>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Case</th>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Scorer</th>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Score</th>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Passed</th>
                </tr>
              </thead>
              <tbody>
                {results.results.map(r => (
                  <tr key={r.evalCaseId} style={{ borderTop: `1px solid ${colors.surface0}` }}>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.text, fontFamily: dataFontFamily }}>{r.evalCaseId.slice(0, 8)}…</td>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.overlay0, fontFamily: uiFontFamily }}>{r.scorerType}</td>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.text, fontFamily: dataFontFamily }}>{r.score}</td>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: r.passed ? colors.signalGreen : colors.alertRed, fontFamily: uiFontFamily }}>
                      {r.passed ? 'Pass' : 'Fail'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      )}

      {regression && (
        <Panel>
          <div style={{ marginBottom: 8 }}>
            <Label style={{ fontWeight: 400 }}>Regression vs. Baseline</Label>
          </div>
          <div style={{ fontSize: 13, color: colors.text, marginBottom: 10, fontFamily: uiFontFamily }}>
            Pass rate <span style={{ fontFamily: dataFontFamily }}>{(regression.currentPassRate * 100).toFixed(0)}%</span> vs baseline{' '}
            <span style={{ fontFamily: dataFontFamily }}>{(regression.baselinePassRate * 100).toFixed(0)}%</span>{' '}
            <span style={{ fontFamily: dataFontFamily, color: regression.passRateDelta < 0 ? colors.alertRed : colors.signalGreen }}>
              ({regression.passRateDelta >= 0 ? '+' : ''}{(regression.passRateDelta * 100).toFixed(0)}pp)
            </span>
          </div>
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12, fontFamily: uiFontFamily }}>
              <thead>
                <tr style={{ color: colors.overlay0, textAlign: 'left' }}>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Case</th>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Current</th>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Baseline</th>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Delta</th>
                  <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Status</th>
                </tr>
              </thead>
              <tbody>
                {regression.caseDiffs.map(d => (
                  <tr key={d.evalCaseId} style={{ borderTop: `1px solid ${colors.surface0}` }}>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.text, fontFamily: dataFontFamily }}>{d.evalCaseId.slice(0, 8)}…</td>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.text, fontFamily: dataFontFamily }}>{d.currentScore}</td>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.overlay0, fontFamily: dataFontFamily }}>{d.baselineScore ?? '—'}</td>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.overlay0, fontFamily: dataFontFamily }}>{d.scoreDelta ?? '—'}</td>
                    <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: d.regressed ? colors.alertRed : d.isNewCase ? colors.beaconYellow : colors.signalGreen, fontFamily: uiFontFamily }}>
                      {d.regressed ? 'Regressed' : d.isNewCase ? 'New case' : 'OK'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Panel>
      )}
      {regressionError && <ErrorBanner>{regressionError}</ErrorBanner>}
    </div>
  )
}

function PostHocView() {
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [agentType, setAgentType] = useState('')
  const [rubric, setRubric] = useState('')
  const [maxTraces, setMaxTraces] = useState(100)
  const [forceRescore, setForceRescore] = useState(false)
  const [error, setError] = useState(null)
  const [runId, setRunId] = useState(null)
  const [summary, setSummary] = useState(null)
  const [summaryError, setSummaryError] = useState(null)

  const agentTypes = ['Orchestrator', 'Research', 'Writer', 'Code', 'Data', 'Browser']

  const submit = () => {
    setError(null)
    setSummary(null)
    setSummaryError(null)
    authenticatedFetch(`${API_BASE}/post-hoc-scoring`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        dateFrom: dateFrom ? new Date(dateFrom).toISOString() : null,
        dateTo: dateTo ? new Date(dateTo).toISOString() : null,
        agentType: agentType || null,
        traceIds: null,
        scorerType: 'LlmJudge',
        rubric,
        passThreshold: null,
        maxTraces: Number(maxTraces),
        forceRescore,
      }),
    })
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? `Failed: ${res.status}`)
        return res.json()
      })
      .then(data => setRunId(data.evalRunId))
      .catch(err => setError(err.message))
  }

  const refreshSummary = () => {
    if (!runId) return
    setSummaryError(null)
    authenticatedFetch(`${API_BASE}/eval-runs/${runId}/posthoc-summary`)
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? `Failed: ${res.status}`)
        return res.json()
      })
      .then(setSummary)
      .catch(err => setSummaryError(err.message))
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <Panel>
        <div style={{ marginBottom: 8 }}>
          <Label style={{ fontWeight: 400 }}>Score historical traces — judge-only, no re-execution</Label>
        </div>
        <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>From</Label>
            </div>
            <input type="datetime-local" value={dateFrom} onChange={e => setDateFrom(e.target.value)} style={selectStyle} />
          </div>
          <div>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>To</Label>
            </div>
            <input type="datetime-local" value={dateTo} onChange={e => setDateTo(e.target.value)} style={selectStyle} />
          </div>
          <div>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>Agent type</Label>
            </div>
            <select value={agentType} onChange={e => setAgentType(e.target.value)} style={selectStyle}>
              <option value="">Any</option>
              {agentTypes.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
          <div>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>Max traces</Label>
            </div>
            <input
              type="number" value={maxTraces} onChange={e => setMaxTraces(e.target.value)}
              style={{ ...selectStyle, width: 90 }}
            />
          </div>
        </div>
        <div style={{ marginTop: 10 }}>
          <div style={{ marginBottom: 8 }}>
            <Label style={{ fontWeight: 400 }}>Rubric</Label>
          </div>
          <TextArea
            value={rubric}
            onChange={e => setRubric(e.target.value)}
            placeholder="e.g. Was the tool call appropriate given the user's request?"
            rows={3}
            style={{ padding: '6px 10px', fontSize: 12, outline: 'none', width: '100%' }}
          />
        </div>
        <div style={{ marginTop: spacing.sm }}>
          <Toggle
            options={[
              { value: false, label: 'Reuse scores' },
              { value: true, label: 'Force re-score' },
            ]}
            value={forceRescore}
            onChange={setForceRescore}
            aria-label="Force re-score behavior for already-scored traces"
          />
          <StateText tone="muted" style={{ fontSize: 12, marginTop: spacing.xs }}>
            Force re-score supersedes any prior post-hoc score for the same trace instead of skipping it.
          </StateText>
        </div>
        <Button
          variant="primary"
          onClick={submit}
          disabled={!rubric || !dateFrom || !dateTo}
          style={{ padding: '6px 14px', width: 'auto', fontSize: 12, background: colors.traceBlue, color: colors.crust, marginTop: 10 }}
        >
          Submit post-hoc scoring request
        </Button>
        {error && <ErrorBanner style={{ marginTop: 10 }}>{error}</ErrorBanner>}
      </Panel>

      {runId && (
        <Panel>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={{ marginBottom: 8 }}>
              <Label style={{ fontWeight: 400 }}>Run {runId.slice(0, 8)}…</Label>
            </div>
            <Button
              variant="primary"
              onClick={refreshSummary}
              style={{ padding: '6px 14px', width: 'auto', fontSize: 12, background: colors.traceBlue, color: colors.crust }}
            >
              Refresh summary
            </Button>
          </div>
          {summaryError && <ErrorBanner>{summaryError}</ErrorBanner>}
          {summary && (
            <div style={{ marginTop: 10 }}>
              <div style={{ fontSize: 13, color: colors.text, fontFamily: uiFontFamily }}>
                Status: {summary.status} —{' '}
                <span style={{ fontFamily: dataFontFamily }}>{summary.scoredCount}</span> scored,{' '}
                <span style={{ fontFamily: dataFontFamily }}>{summary.skippedAlreadyScoredCount}</span> skipped
                (already scored)
              </div>
              <div style={{ fontSize: 20, fontWeight: 700, color: colors.text, marginTop: spacing.xs, fontFamily: uiFontFamily }}>
                <span style={{ fontFamily: dataFontFamily }}>{(summary.passRate * 100).toFixed(0)}%</span> pass rate (
                <span style={{ fontFamily: dataFontFamily }}>{summary.passedCount}/{summary.scoredCount}</span>)
              </div>
              <div style={{ overflowX: 'auto' }}>
                <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12, fontFamily: uiFontFamily }}>
                  <thead>
                    <tr style={{ color: colors.overlay0, textAlign: 'left' }}>
                      <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Score range</th>
                      <th style={{ padding: `${spacing.xs} ${spacing.sm}` }}>Count</th>
                    </tr>
                  </thead>
                  <tbody>
                    {summary.scoreDistribution.map(b => (
                      <tr key={b.range} style={{ borderTop: `1px solid ${colors.surface0}` }}>
                        <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.text, fontFamily: dataFontFamily }}>{b.range}</td>
                        <td style={{ padding: `${spacing.xs} ${spacing.sm}`, color: colors.text, fontFamily: dataFontFamily }}>{b.count}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </Panel>
      )}
    </div>
  )
}

export default function EvalsPage() {
  const [subView, setSubView] = useState('suites')
  const [suites, setSuites] = useState(null)
  const [selectedSuiteId, setSelectedSuiteId] = useState(null)
  const [selectedRunId, setSelectedRunId] = useState(null)

  useEffect(() => {
    authenticatedFetch(`${API_BASE}/eval-suites`)
      .then(res => res.json())
      .then(data => setSuites(data.suites))
      .catch(() => setSuites([]))
  }, [])

  return (
    <div>
      <SubNav subView={subView} setSubView={setSubView} />
      {subView === 'suites' && (
        <SuitesView suites={suites} selectedSuiteId={selectedSuiteId} onSelect={setSelectedSuiteId} />
      )}
      {subView === 'run' && (
        <RunView
          suites={suites}
          selectedSuiteId={selectedSuiteId}
          onRunTriggered={runId => { setSelectedRunId(runId); setSubView('results') }}
        />
      )}
      {subView === 'results' && (
        <ResultsView
          suites={suites}
          selectedSuiteId={selectedSuiteId}
          selectedRunId={selectedRunId}
          onSelectRun={setSelectedRunId}
        />
      )}
      {subView === 'posthoc' && <PostHocView />}
    </div>
  )
}
