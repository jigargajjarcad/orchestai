import { useState } from 'react'
import { setApiKey } from './apiKey'

export default function ApiKeyPrompt({ onSubmitted }) {
  const [value, setValue] = useState('')

  const submit = () => {
    if (!value.trim()) return
    setApiKey(value.trim())
    onSubmitted()
  }

  return (
    <div style={{ padding: 40, maxWidth: 480, margin: '80px auto', fontFamily: 'sans-serif' }}>
      <h2 style={{ color: '#cdd6f4' }}>OrchestAI — API Key Required</h2>
      <p style={{ color: '#6c7086', fontSize: 13 }}>
        Enter your API key. It is held in memory for this browser session only — never saved to
        disk, never sent anywhere except as an Authorization header on requests to this API.
        Refreshing the page will require re-entering it. This is a temporary development flow,
        not a production authentication design.
      </p>
      <input
        type="password"
        value={value}
        onChange={e => setValue(e.target.value)}
        onKeyDown={e => e.key === 'Enter' && submit()}
        placeholder="orch_live_..."
        autoComplete="off"
        style={{
          width: '100%', padding: '10px 12px', fontSize: 14, borderRadius: 6,
          border: '1px solid #313244', background: '#181825', color: '#cdd6f4',
        }}
      />
      <button
        onClick={submit}
        style={{
          marginTop: 12, padding: '8px 16px', borderRadius: 6, border: 'none',
          background: '#89b4fa', color: '#11111b', fontWeight: 700, cursor: 'pointer',
        }}
      >
        Continue
      </button>
    </div>
  )
}
