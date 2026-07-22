#!/usr/bin/env bash
# Bootstraps a local dev tenant + API key against a running OrchestAI.API — pure automation of
# the README Quick Start's manual curl steps (mint tenant, mint API key, print the raw key).
# Calls the exact same admin-gated endpoints (CreateTenantCommand/CreateApiKeyCommand via
# AdminController, gated by RequireAdminSecretFilter's X-Admin-Secret header) a developer would
# hit by hand — no new capability, no new API surface, no change to the tenant-isolation or
# authentication model. Does NOT submit a task; stops after printing the credentials so you can
# paste the rawKey into the frontend or use it in your own curl calls.
#
# Prerequisites: OrchestAI.API already running locally with a real Admin__BootstrapSecret set
# (see README.md's Quick Start, step 3).
#
# Usage:
#   ADMIN_SECRET=<value matching Admin__BootstrapSecret> ./scripts/bootstrap-local-dev.sh

set -euo pipefail

API_BASE="http://localhost:5000/api/v1"
ADMIN_SECRET="${ADMIN_SECRET:?Set ADMIN_SECRET to match the API Admin__BootstrapSecret value}"
TENANT_NAME="${TENANT_NAME:-Local Dev}"
TENANT_SLUG="${TENANT_SLUG:-local-dev}"

echo "== Bootstrapping tenant + API key against $API_BASE ==" >&2

TENANT_JSON=$(curl -sf -X POST "$API_BASE/admin/tenants" \
  -H "X-Admin-Secret: $ADMIN_SECRET" -H "Content-Type: application/json" \
  -d "$(jq -n --arg n "$TENANT_NAME" --arg s "$TENANT_SLUG" '{name:$n, slug:$s}')")
TENANT_ID=$(echo "$TENANT_JSON" | jq -r '.tenantId')
echo "Tenant created: $TENANT_ID ($TENANT_NAME / $TENANT_SLUG)" >&2

APIKEY_JSON=$(curl -sf -X POST "$API_BASE/admin/api-keys" \
  -H "X-Admin-Secret: $ADMIN_SECRET" -H "Content-Type: application/json" \
  -d "$(jq -n --arg t "$TENANT_ID" '{tenantId:$t, displayName:"local-dev-key"}')")
RAW_KEY=$(echo "$APIKEY_JSON" | jq -r '.rawKey')

echo "" >&2
echo "== Done. This key is shown exactly once — it cannot be retrieved again. ==" >&2
echo "" >&2
echo "Paste this into the frontend's API key prompt, or use it as a Bearer token:" >&2
echo "" >&2
echo "$RAW_KEY"
