# Phase 3 Sports Investigation — Demo Runbook

Prerequisites: Postgres running and migrated (Task 1), seed data loaded (Task 2), `OrchestAI.API`
running locally with a real `Anthropic__ApiKey` and `Admin__BootstrapSecret` set (see Step 0).

Note on port: don't rely on `dotnet run --urls http://localhost:5100` (or similar `--urls`
overrides) — `appsettings.Development.json` configures an explicit Kestrel endpoint
(`Kestrel:Endpoints:Http:Url` = `http://localhost:5000`), which wins over both the `--urls` flag
and `Program.cs`'s own `PORT`-based `UseUrls` fallback. The API binds to **port 5000** in Development
regardless of what you pass on the command line.

## 0. Bootstrap auth (tenant + API key)

`TenantAuthenticationMiddleware` requires a `Bearer` token resolving to an active tenant's API key
on every `/api/v1/*` route except `/health`, `/swagger`, `/api/v1/admin`, and any route ending in
`/stream`. The well-known `DevUserId` used below is a valid `Users` row, but the `Users` table has
no tenant association at all — tenant context comes entirely from the Bearer token, independent of
`userId`. So a tenant + API key must be minted first via the admin bootstrap endpoints.

Start the API with an admin secret set, e.g.:

```
Admin__BootstrapSecret=some-local-secret dotnet run --project src/OrchestAI.API
```

Both admin calls below require that same secret on an `X-Admin-Secret` header (this is a separate,
one-time bootstrap secret — distinct from the tenant API key it produces, and only ever used
against the admin-only surface, which `RequireAdminSecretFilter` gates instead of tenant auth).

Create a tenant:

```
POST /api/v1/admin/tenants
X-Admin-Secret: some-local-secret
{
  "name": "Phase 3 Demo Tenant",
  "slug": "phase3-demo"
}
```

Response (201):

```
{
  "tenantId": "...",
  "name": "Phase 3 Demo Tenant",
  "slug": "phase3-demo",
  "createdAt": "..."
}
```

Mint an API key for that tenant:

```
POST /api/v1/admin/api-keys
X-Admin-Secret: some-local-secret
{
  "tenantId": "<tenantId from above>",
  "displayName": "phase3-demo-key"
}
```

Response (201):

```
{
  "apiKeyId": "...",
  "rawKey": "orch_live_...",
  "publicKeyId": "...",
  "createdAt": "..."
}
```

`rawKey` is returned exactly once — it can never be retrieved again. Use it as
`Authorization: Bearer <rawKey>` on every `/api/v1/tasks*` call in the steps below.

## 1. Create the task

```
POST /api/v1/tasks
Authorization: Bearer <rawKey>
{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Sports performance investigation",
  "userPrompt": "Investigate Kawhi Leonard's performance over his December 12, 2017 through January 13, 2018 return-from-injury stretch with the San Antonio Spurs — his last 9 games in that window (query the SportsPerformanceGames table for AthleteName = 'Kawhi Leonard', ordered by GameNumber). Independently: (1) analyze the raw performance trend in minutes played and points across these games, and (2) research any publicly reported context (injuries, role changes) around this December 2017-January 2018 stretch. Then explicitly compare the two: does the reported context explain the statistical trend, or is there a contradiction between what the numbers show and what the public context suggests? Produce a final conclusion that traces each claim back to whether it came from the statistical data or the contextual research."
}
```

Note: `DevUserId` (`3fa85f64-5717-4562-b3fc-2c963f66afa6`, from `DatabaseSeeder.cs:9`) is the same
well-known dev user ID used throughout this project's demos.

## 2. Start it

```
POST /api/v1/tasks/{id}/start
Authorization: Bearer <rawKey>
```

## 3. Read the result

```
GET /api/v1/tasks/{id}
Authorization: Bearer <rawKey>
```

Check `finalResult` for the reconciled conclusion, and (optionally) `GET /api/v1/tasks/{id}?includeMessages=true&includeToolCalls=true`
(same `Authorization: Bearer <rawKey>` header) to see each agent's actual
`db_query`/`perplexity_search`/`firecrawl_scrape` calls for full traceability.

## What to check on each run

- Did the orchestrator's plan (visible via the `orchestrator_plan` SSE event, or `includeMessages=true`)
  select exactly `Data` and `Research` — not fewer, not more?
- Did `DataAgent` actually call `db_query` against `SportsPerformanceGames` (not hallucinate numbers)?
- Did the final result explicitly address agreement/contradiction between the two sources, citing
  which agent each claim came from?
- If Firecrawl/Perplexity keys are not configured, does `ResearchAgent`'s contribution get
  correctly labeled as unsourced model knowledge rather than presented as cited research?
