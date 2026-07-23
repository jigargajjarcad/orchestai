// frontend/src/EvalsPage.jsx
import { useState, useEffect } from 'react'
import { authenticatedFetch } from './apiKey'
import { colors, radii } from './theme/tokens'
import { Panel } from './components/Panel'
import { Label } from './components/Label'
import { Input, TextArea } from './components/Input'
import { Button } from './components/Button'
import { Nav, NavItem } from './components/NavItem'
import { StateText } from './components/StateText'

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
      {suites.length === 0 && (
        <StateText tone="muted" style={{ color: colors.overlay0, fontSize: 13 }}>
          No suites yet — create one via <code>POST /api/v1/eval-suites</code>.
        </StateText>
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
            <div style={{ fontSize: 13, fontWeight: 700, color: colors.text }}>{s.name}</div>
            <div style={{ fontSize: 11, color: colors.overlay0 }}>
              {s.targetAgentType} · {s.description}
            </div>
          </div>
        ))}
      </div>
    </Panel>
  )
}

function RunView({ suites, selectedSuiteId, onRunTriggered }) {
  const [runs, setRuns] = useState([])
  const [subjectVersion, setSubjectVersion] = useState('')
  const [baselineRunId, setBaselineRunId] = useState('')
  const [error, setError] = useState(null)
  const suite = suites.find(s => s.id === selectedSuiteId)

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
        <StateText tone="muted" style={{ color: colors.overlay0, fontSize: 13 }}>Select a suite first.</StateText>
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
      {error && <StateText tone="error" style={{ fontSize: 12, marginTop: 10 }}>{error}</StateText>}
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

      {resultsError && (
        <Panel style={{ color: colors.overlay0, fontSize: 12 }}>{resultsError}</Panel>
      )}

      {results && (
        <Panel>
          <div style={{ marginBottom: 8 }}>
            <Label style={{ fontWeight: 400 }}>Pass/Fail Summary</Label>
          </div>
          <div style={{ fontSize: 20, fontWeight: 700, color: colors.text }}>
            {passCount} / {results.results.length} passed
          </div>
          <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ color: colors.overlay0, textAlign: 'left' }}>
                <th style={{ padding: '4px 8px' }}>Case</th>
                <th style={{ padding: '4px 8px' }}>Scorer</th>
                <th style={{ padding: '4px 8px' }}>Score</th>
                <th style={{ padding: '4px 8px' }}>Passed</th>
              </tr>
            </thead>
            <tbody>
              {results.results.map(r => (
                <tr key={r.evalCaseId} style={{ borderTop: `1px solid ${colors.surface0}` }}>
                  <td style={{ padding: '4px 8px', color: colors.text }}>{r.evalCaseId.slice(0, 8)}…</td>
                  <td style={{ padding: '4px 8px', color: colors.overlay0 }}>{r.scorerType}</td>
                  <td style={{ padding: '4px 8px', color: colors.text }}>{r.score}</td>
                  <td style={{ padding: '4px 8px', color: r.passed ? colors.signalGreen : colors.alertRed }}>
                    {r.passed ? 'Pass' : 'Fail'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      )}

      {regression && (
        <Panel>
          <div style={{ marginBottom: 8 }}>
            <Label style={{ fontWeight: 400 }}>Regression vs. Baseline</Label>
          </div>
          <div style={{ fontSize: 13, color: colors.text, marginBottom: 10 }}>
            Pass rate {(regression.currentPassRate * 100).toFixed(0)}% vs baseline{' '}
            {(regression.baselinePassRate * 100).toFixed(0)}%{' '}
            <span style={{ color: regression.passRateDelta < 0 ? colors.alertRed : colors.signalGreen }}>
              ({regression.passRateDelta >= 0 ? '+' : ''}{(regression.passRateDelta * 100).toFixed(0)}pp)
            </span>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ color: colors.overlay0, textAlign: 'left' }}>
                <th style={{ padding: '4px 8px' }}>Case</th>
                <th style={{ padding: '4px 8px' }}>Current</th>
                <th style={{ padding: '4px 8px' }}>Baseline</th>
                <th style={{ padding: '4px 8px' }}>Delta</th>
                <th style={{ padding: '4px 8px' }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {regression.caseDiffs.map(d => (
                <tr key={d.evalCaseId} style={{ borderTop: `1px solid ${colors.surface0}` }}>
                  <td style={{ padding: '4px 8px', color: colors.text }}>{d.evalCaseId.slice(0, 8)}…</td>
                  <td style={{ padding: '4px 8px', color: colors.text }}>{d.currentScore}</td>
                  <td style={{ padding: '4px 8px', color: colors.overlay0 }}>{d.baselineScore ?? '—'}</td>
                  <td style={{ padding: '4px 8px', color: colors.overlay0 }}>{d.scoreDelta ?? '—'}</td>
                  <td style={{ padding: '4px 8px', color: d.regressed ? colors.alertRed : d.isNewCase ? colors.beaconYellow : colors.signalGreen }}>
                    {d.regressed ? 'Regressed' : d.isNewCase ? 'New case' : 'OK'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      )}
      {regressionError && (
        <Panel style={{ color: colors.overlay0, fontSize: 12 }}>{regressionError}</Panel>
      )}
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
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 10, fontSize: 12, color: colors.text }}>
          <input type="checkbox" checked={forceRescore} onChange={e => setForceRescore(e.target.checked)} />
          Force re-score (supersedes any prior post-hoc score for the same trace instead of skipping it)
        </label>
        <Button
          variant="primary"
          onClick={submit}
          disabled={!rubric || !dateFrom || !dateTo}
          style={{ padding: '6px 14px', width: 'auto', fontSize: 12, background: colors.traceBlue, color: colors.crust, marginTop: 10 }}
        >
          Submit post-hoc scoring request
        </Button>
        {error && <StateText tone="error" style={{ fontSize: 12, marginTop: 10 }}>{error}</StateText>}
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
          {summaryError && <StateText tone="muted" style={{ color: colors.overlay0, fontSize: 12 }}>{summaryError}</StateText>}
          {summary && (
            <div style={{ marginTop: 10 }}>
              <div style={{ fontSize: 13, color: colors.text }}>
                Status: {summary.status} — {summary.scoredCount} scored, {summary.skippedAlreadyScoredCount} skipped
                (already scored)
              </div>
              <div style={{ fontSize: 20, fontWeight: 700, color: colors.text, marginTop: 6 }}>
                {(summary.passRate * 100).toFixed(0)}% pass rate ({summary.passedCount}/{summary.scoredCount})
              </div>
              <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ color: colors.overlay0, textAlign: 'left' }}>
                    <th style={{ padding: '4px 8px' }}>Score range</th>
                    <th style={{ padding: '4px 8px' }}>Count</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.scoreDistribution.map(b => (
                    <tr key={b.range} style={{ borderTop: `1px solid ${colors.surface0}` }}>
                      <td style={{ padding: '4px 8px', color: colors.text }}>{b.range}</td>
                      <td style={{ padding: '4px 8px', color: colors.text }}>{b.count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </Panel>
      )}
    </div>
  )
}

export default function EvalsPage() {
  const [subView, setSubView] = useState('suites')
  const [suites, setSuites] = useState([])
  const [selectedSuiteId, setSelectedSuiteId] = useState(null)
  const [selectedRunId, setSelectedRunId] = useState(null)

  useEffect(() => {
    authenticatedFetch(`${API_BASE}/eval-suites`)
      .then(res => res.json())
      .then(data => setSuites(data.suites))
      .catch(() => setSuites([]))
  }, [])

  return (
    <div style={{ padding: '20px 24px', maxWidth: 1100 }}>
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
