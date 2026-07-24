import { useState } from 'react'
import { setApiKey } from './apiKey'
import { Input } from './components/Input'
import { Button } from './components/Button'

export default function ApiKeyPrompt({ onSubmitted }) {
  const [value, setValue] = useState('')

  const submit = () => {
    if (!value.trim()) return
    setApiKey(value.trim())
    onSubmitted()
  }

  return (
    <div style={{ padding: 40, maxWidth: 480, margin: '80px auto', fontFamily: 'sans-serif' }}>
      <h2 style={{ color: '#cdd6f4' }}>OrchestAI</h2>
      <p style={{ color: '#585b70', margin: 0, fontSize: 11 }}>Multi-agent CQRS orchestration · .NET 8</p>
      <p style={{ color: '#6c7086', fontSize: 13 }}>
        An API key is required to run agents and query stored data. This instance has no
        self-service signup — keys are provisioned out-of-band by an operator. If you don't
        have one, ask whoever set up this deployment. Running it yourself locally: see the
        README's Quick Start (scripts/bootstrap-local-dev.sh mints one).
      </p>
      <p style={{ color: '#6c7086', fontSize: 13 }}>
        Held in memory for this browser session only — never saved to disk, never sent anywhere
        except as an Authorization header on requests to this API. Refreshing the page will
        require re-entering it. This is a temporary development flow, not a production
        authentication design.
      </p>
      <Input
        type="password"
        value={value}
        onChange={e => setValue(e.target.value)}
        onKeyDown={e => e.key === 'Enter' && submit()}
        placeholder="orch_live_..."
        autoComplete="off"
        style={{ padding: '10px 12px', fontSize: 14 }}
      />
      <Button variant="primary" onClick={submit} style={{ width: 'auto', marginTop: 12, padding: '8px 16px' }}>
        Continue
      </Button>
    </div>
  )
}
