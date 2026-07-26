import { useState } from 'react'
import { setApiKey } from './apiKey'
import { Input } from './components/Input'
import { Button } from './components/Button'
import { Card } from './components/Card'
import { Label } from './components/Label'
import { colors, spacing, radii, surface, uiFontFamily } from './theme/tokens'

export default function ApiKeyPrompt({ onSubmitted }) {
  const [value, setValue] = useState('')

  const submit = () => {
    if (!value.trim()) return
    setApiKey(value.trim())
    onSubmitted()
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: surface.canvas,
        padding: spacing.xl,
        boxSizing: 'border-box',
      }}
    >
      {/* This gate is the app's single full-page entry surface (the sole thing
          App.jsx renders while `!keySet`) rather than a panel nested inside
          other chrome — so its Card is bumped from the standard top-level-
          panel radius (radiusForElement.card, 8px) to the bigger
          `radii.xxl`/`bigSurface` class (12px) DESIGN.md reserves for
          canvas-scale frames/overlays. */}
      <Card level="raised" padding={spacing.xxl} style={{ maxWidth: 480, width: '100%', borderRadius: radii.xxl }}>
        <h2 style={{ margin: 0, color: colors.text, fontFamily: uiFontFamily }}>OrchestAI</h2>
        <div style={{ marginTop: spacing.xs }}>
          <Label style={{ color: colors.surface2, fontWeight: 400 }}>Multi-agent CQRS orchestration · .NET 8</Label>
        </div>
        <p style={{ margin: `${spacing.lg} 0 0`, color: colors.subtext0, fontSize: 13, fontFamily: uiFontFamily }}>
          An API key is required to run agents and query stored data. This instance has no
          self-service signup — keys are provisioned out-of-band by an operator. If you don't
          have one, ask whoever set up this deployment. Running it yourself locally: see the
          README's Quick Start (scripts/bootstrap-local-dev.sh mints one).
        </p>
        <p style={{ margin: `${spacing.md} 0 0`, color: colors.subtext0, fontSize: 13, fontFamily: uiFontFamily }}>
          Held in memory for this browser session only — never saved to disk, never sent anywhere
          except as an Authorization header on requests to this API. Refreshing the page will
          require re-entering it. This is a temporary development flow, not a production
          authentication design.
        </p>
        <div style={{ marginTop: spacing.lg }}>
          <Input
            label="API key"
            type="password"
            value={value}
            onChange={e => setValue(e.target.value)}
            onKeyDown={e => e.key === 'Enter' && submit()}
            placeholder="orch_live_..."
            autoComplete="off"
          />
        </div>
        <Button variant="primary" onClick={submit} fullWidth={false} style={{ marginTop: spacing.md }}>
          Continue
        </Button>
      </Card>
    </div>
  )
}
