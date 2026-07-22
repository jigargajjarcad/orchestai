#!/usr/bin/env bash
# Runs the Phase 3 Sports/Athlete Performance demo end-to-end against a locally running
# OrchestAI.API. Pure automation of docs/phase3-sports-demo-runbook.md's existing manual curl
# steps (bootstrap tenant + API key, submit the fixed question, poll, print the result) — no new
# capability, no new API surface. Disposable/optional: the runbook alone is still sufficient.
#
# Prerequisites (see docs/phase3-sports-demo-runbook.md):
#   - Postgres running and migrated, SportsPerformanceGames seeded (scripts/seed-sports-demo-data.sql)
#   - OrchestAI.API running locally with a real Anthropic__ApiKey, Tools__Perplexity__ApiKey
#     (optional but recommended for live cited research), and Admin__BootstrapSecret set.
#
# Usage:
#   ADMIN_SECRET=<value matching Admin__BootstrapSecret> ./scripts/run-sports-demo.sh

set -euo pipefail

API_BASE="http://localhost:5000/api/v1"
ADMIN_SECRET="${ADMIN_SECRET:?Set ADMIN_SECRET to match the API Admin__BootstrapSecret value}"
DEV_USER_ID="3fa85f64-5717-4562-b3fc-2c963f66afa6"

# Load-bearing wording — do not shorten. See docs/phase3-sports-demo-runbook.md's own note: an
# earlier attempt using only "last 9 games" (no year) caused live Perplexity research to drift to
# the athlete's current season instead of the seeded 2017-18 stretch.
USER_PROMPT="Investigate Kawhi Leonard's performance over his December 12, 2017 through January 13, 2018 return-from-injury stretch with the San Antonio Spurs — his last 9 games in that window (query the SportsPerformanceGames table for AthleteName = 'Kawhi Leonard', ordered by GameNumber). Independently: (1) analyze the raw performance trend in minutes played and points across these games, and (2) research any publicly reported context (injuries, role changes) around this December 2017-January 2018 stretch. Then explicitly compare the two: does the reported context explain the statistical trend, or is there a contradiction between what the numbers show and what the public context suggests? Produce a final conclusion that traces each claim back to whether it came from the statistical data or the contextual research."

echo "== Step 0: bootstrap tenant + API key =="
TENANT_JSON=$(curl -sf -X POST "$API_BASE/admin/tenants" \
  -H "X-Admin-Secret: $ADMIN_SECRET" -H "Content-Type: application/json" \
  -d '{"name":"Sports Demo Tenant","slug":"sports-demo-run"}')
TENANT_ID=$(echo "$TENANT_JSON" | jq -r '.tenantId')
echo "Tenant created: $TENANT_ID"

APIKEY_JSON=$(curl -sf -X POST "$API_BASE/admin/api-keys" \
  -H "X-Admin-Secret: $ADMIN_SECRET" -H "Content-Type: application/json" \
  -d "{\"tenantId\":\"$TENANT_ID\",\"displayName\":\"sports-demo-key\"}")
RAW_KEY=$(echo "$APIKEY_JSON" | jq -r '.rawKey')
echo "API key minted (not printed)."

echo "== Step 1: submit the task =="
CREATE_JSON=$(curl -sf -X POST "$API_BASE/tasks" \
  -H "Authorization: Bearer $RAW_KEY" -H "Content-Type: application/json" \
  -d "$(jq -n --arg u "$DEV_USER_ID" --arg p "$USER_PROMPT" \
    '{userId:$u, title:"Sports performance investigation", userPrompt:$p}')")
TASK_ID=$(echo "$CREATE_JSON" | jq -r '.id')
echo "Task created: $TASK_ID"

echo "== Step 2: start it =="
curl -sf -X POST "$API_BASE/tasks/$TASK_ID/start" -H "Authorization: Bearer $RAW_KEY" > /dev/null
echo "Started. Polling for completion (this involves real, live LLM + research calls — can take 1-3 minutes)..."

echo "== Step 3: poll until complete =="
while true; do
  READ_JSON=$(curl -sf "$API_BASE/tasks/$TASK_ID" -H "Authorization: Bearer $RAW_KEY")
  STATUS=$(echo "$READ_JSON" | jq -r '.status')
  echo "  status: $STATUS"
  if [[ "$STATUS" == "Completed" || "$STATUS" == "Failed" ]]; then
    break
  fi
  sleep 5
done

echo ""
echo "== Result =="
echo "$READ_JSON" | jq '{status, totalCostUsd, totalInputTokens, totalOutputTokens, finalResult, errorMessage}'
echo ""
echo "Full evidence trail (tool calls, per-agent messages):"
echo "  curl -s \"$API_BASE/tasks/$TASK_ID?includeMessages=true&includeToolCalls=true\" -H \"Authorization: Bearer $RAW_KEY\" | jq"
