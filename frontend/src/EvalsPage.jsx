// frontend/src/EvalsPage.jsx
import { useState, useEffect } from 'react'
import { authenticatedFetch } from './apiKey'

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`

const panelStyle = {
  background: '#1e1e2e',
  border: '1px solid #313244',
  borderRadius: 8,
  padding: '16px 18px',
}

const labelStyle = {
  fontSize: 11,
  color: '#6c7086',
  textTransform: 'uppercase',
  letterSpacing: '0.07em',
  marginBottom: 8,
}

const selectStyle = {
  padding: '6px 10px',
  borderRadius: 6,
  border: '1px solid #313244',
  background: '#181825',
  color: '#cdd6f4',
  fontSize: 12,
  outline: 'none',
}

const buttonStyle = {
  padding: '6px 14px',
  borderRadius: 6,
  border: 'none',
  background: '#89b4fa',
  color: '#11111b',
  fontSize: 12,
  fontWeight: 700,
  cursor: 'pointer',
}

function SubNav({ subView, setSubView }) {
  const tabs = [['suites', 'Suites'], ['run', 'Run'], ['results', 'Results'], ['posthoc', 'Post-Hoc']]
  return (
    <div style={{ display: 'flex', gap: 4, marginBottom: 20, borderBottom: '1px solid #1e1e2e', paddingBottom: 12 }}>
      {tabs.map(([key, label]) => (
        <button
          key={key}
          onClick={() => setSubView(key)}
          style={{
            background: subView === key ? '#313244' : 'transparent',
            color: subView === key ? '#cdd6f4' : '#6c7086',
            border: 'none', borderRadius: 6, padding: '6px 14px', fontSize: 12, cursor: 'pointer',
            fontWeight: subView === key ? 700 : 400,
          }}
        >
          {label}
        </button>
      ))}
    </div>
  )
}

function SuitesView({ suites, selectedSuiteId, onSelect }) {
  return (
    <div style={panelStyle}>
      <div style={labelStyle}>Eval Suites</div>
      {suites.length === 0 && (
        <p style={{ color: '#6c7086', fontSize: 13 }}>
          No suites yet — create one via <code>POST /api/v1/eval-suites</code>.
        </p>
      )}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {suites.map(s => (
          <div
            key={s.id}
            onClick={() => onSelect(s.id)}
            style={{
              padding: '10px 12px', borderRadius: 6, cursor: 'pointer',
              background: selectedSuiteId === s.id ? '#313244' : '#181825',
              border: selectedSuiteId === s.id ? '1px solid #89b4fa' : '1px solid #313244',
            }}
          >
            <div style={{ fontSize: 13, fontWeight: 700, color: '#cdd6f4' }}>{s.name}</div>
            <div style={{ fontSize: 11, color: '#6c7086' }}>
              {s.targetAgentType} · {s.description}
            </div>
          </div>
        ))}
      </div>
    </div>
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
    return <div style={panelStyle}><p style={{ color: '#6c7086', fontSize: 13 }}>Select a suite first.</p></div>
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
    <div style={panelStyle}>
      <div style={labelStyle}>Trigger a run — {suite?.name}</div>
      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <div>
          <div style={labelStyle}>Subject version</div>
          <input
            value={subjectVersion}
            onChange={e => setSubjectVersion(e.target.value)}
            placeholder="git SHA or prompt version"
            style={{ ...selectStyle, width: 220 }}
          />
        </div>
        <div>
          <div style={labelStyle}>Baseline run</div>
          <select value={baselineRunId} onChange={e => setBaselineRunId(e.target.value)} style={{ ...selectStyle, width: 260 }}>
            <option value="">None (first run for this suite)</option>
            {runs.map(r => (
              <option key={r.id} value={r.id}>
                {r.subjectVersion} — {r.status} — {new Date(r.triggeredAt).toLocaleString()}
              </option>
            ))}
          </select>
        </div>
        <button onClick={trigger} disabled={!subjectVersion} style={buttonStyle}>Run suite</button>
      </div>
      {error && <p style={{ color: '#f38ba8', fontSize: 12, marginTop: 10 }}>{error}</p>}
    </div>
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
      <div style={panelStyle}>
        <div style={labelStyle}>Run</div>
        <select value={selectedRunId ?? ''} onChange={e => onSelectRun(e.target.value)} style={{ ...selectStyle, width: '100%' }}>
          <option value="" disabled>Select a run…</option>
          {runs.map(r => (
            <option key={r.id} value={r.id}>
              {r.subjectVersion} — {r.status} — {new Date(r.triggeredAt).toLocaleString()}
            </option>
          ))}
        </select>
      </div>

      {resultsError && (
        <div style={{ ...panelStyle, color: '#6c7086', fontSize: 12 }}>{resultsError}</div>
      )}

      {results && (
        <div style={panelStyle}>
          <div style={labelStyle}>Pass/Fail Summary</div>
          <div style={{ fontSize: 20, fontWeight: 700, color: '#cdd6f4' }}>
            {passCount} / {results.results.length} passed
          </div>
          <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ color: '#6c7086', textAlign: 'left' }}>
                <th style={{ padding: '4px 8px' }}>Case</th>
                <th style={{ padding: '4px 8px' }}>Scorer</th>
                <th style={{ padding: '4px 8px' }}>Score</th>
                <th style={{ padding: '4px 8px' }}>Passed</th>
              </tr>
            </thead>
            <tbody>
              {results.results.map(r => (
                <tr key={r.evalCaseId} style={{ borderTop: '1px solid #313244' }}>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{r.evalCaseId.slice(0, 8)}…</td>
                  <td style={{ padding: '4px 8px', color: '#6c7086' }}>{r.scorerType}</td>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{r.score}</td>
                  <td style={{ padding: '4px 8px', color: r.passed ? '#a6e3a1' : '#f38ba8' }}>
                    {r.passed ? 'Pass' : 'Fail'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {regression && (
        <div style={panelStyle}>
          <div style={labelStyle}>Regression vs. Baseline</div>
          <div style={{ fontSize: 13, color: '#cdd6f4', marginBottom: 10 }}>
            Pass rate {(regression.currentPassRate * 100).toFixed(0)}% vs baseline{' '}
            {(regression.baselinePassRate * 100).toFixed(0)}%{' '}
            <span style={{ color: regression.passRateDelta < 0 ? '#f38ba8' : '#a6e3a1' }}>
              ({regression.passRateDelta >= 0 ? '+' : ''}{(regression.passRateDelta * 100).toFixed(0)}pp)
            </span>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ color: '#6c7086', textAlign: 'left' }}>
                <th style={{ padding: '4px 8px' }}>Case</th>
                <th style={{ padding: '4px 8px' }}>Current</th>
                <th style={{ padding: '4px 8px' }}>Baseline</th>
                <th style={{ padding: '4px 8px' }}>Delta</th>
                <th style={{ padding: '4px 8px' }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {regression.caseDiffs.map(d => (
                <tr key={d.evalCaseId} style={{ borderTop: '1px solid #313244' }}>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{d.evalCaseId.slice(0, 8)}…</td>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{d.currentScore}</td>
                  <td style={{ padding: '4px 8px', color: '#6c7086' }}>{d.baselineScore ?? '—'}</td>
                  <td style={{ padding: '4px 8px', color: '#6c7086' }}>{d.scoreDelta ?? '—'}</td>
                  <td style={{ padding: '4px 8px', color: d.regressed ? '#f38ba8' : d.isNewCase ? '#f9e2af' : '#a6e3a1' }}>
                    {d.regressed ? 'Regressed' : d.isNewCase ? 'New case' : 'OK'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {regressionError && (
        <div style={{ ...panelStyle, color: '#6c7086', fontSize: 12 }}>{regressionError}</div>
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
      <div style={panelStyle}>
        <div style={labelStyle}>Score historical traces — judge-only, no re-execution</div>
        <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div>
            <div style={labelStyle}>From</div>
            <input type="datetime-local" value={dateFrom} onChange={e => setDateFrom(e.target.value)} style={selectStyle} />
          </div>
          <div>
            <div style={labelStyle}>To</div>
            <input type="datetime-local" value={dateTo} onChange={e => setDateTo(e.target.value)} style={selectStyle} />
          </div>
          <div>
            <div style={labelStyle}>Agent type</div>
            <select value={agentType} onChange={e => setAgentType(e.target.value)} style={selectStyle}>
              <option value="">Any</option>
              {agentTypes.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
          <div>
            <div style={labelStyle}>Max traces</div>
            <input
              type="number" value={maxTraces} onChange={e => setMaxTraces(e.target.value)}
              style={{ ...selectStyle, width: 90 }}
            />
          </div>
        </div>
        <div style={{ marginTop: 10 }}>
          <div style={labelStyle}>Rubric</div>
          <textarea
            value={rubric}
            onChange={e => setRubric(e.target.value)}
            placeholder="e.g. Was the tool call appropriate given the user's request?"
            rows={3}
            style={{ ...selectStyle, width: '100%', resize: 'vertical' }}
          />
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 10, fontSize: 12, color: '#cdd6f4' }}>
          <input type="checkbox" checked={forceRescore} onChange={e => setForceRescore(e.target.checked)} />
          Force re-score (supersedes any prior post-hoc score for the same trace instead of skipping it)
        </label>
        <button onClick={submit} disabled={!rubric || !dateFrom || !dateTo} style={{ ...buttonStyle, marginTop: 10 }}>
          Submit post-hoc scoring request
        </button>
        {error && <p style={{ color: '#f38ba8', fontSize: 12, marginTop: 10 }}>{error}</p>}
      </div>

      {runId && (
        <div style={panelStyle}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={labelStyle}>Run {runId.slice(0, 8)}…</div>
            <button onClick={refreshSummary} style={buttonStyle}>Refresh summary</button>
          </div>
          {summaryError && <p style={{ color: '#6c7086', fontSize: 12 }}>{summaryError}</p>}
          {summary && (
            <div style={{ marginTop: 10 }}>
              <div style={{ fontSize: 13, color: '#cdd6f4' }}>
                Status: {summary.status} — {summary.scoredCount} scored, {summary.skippedAlreadyScoredCount} skipped
                (already scored)
              </div>
              <div style={{ fontSize: 20, fontWeight: 700, color: '#cdd6f4', marginTop: 6 }}>
                {(summary.passRate * 100).toFixed(0)}% pass rate ({summary.passedCount}/{summary.scoredCount})
              </div>
              <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ color: '#6c7086', textAlign: 'left' }}>
                    <th style={{ padding: '4px 8px' }}>Score range</th>
                    <th style={{ padding: '4px 8px' }}>Count</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.scoreDistribution.map(b => (
                    <tr key={b.range} style={{ borderTop: '1px solid #313244' }}>
                      <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{b.range}</td>
                      <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{b.count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
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
