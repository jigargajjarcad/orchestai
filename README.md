# OrchestAI

> Production-ready multi-agent AI orchestration for .NET 8

**The .NET alternative to LangGraph, AutoGen, and CrewAI.** OrchestAI provides a complete CQRS-based framework for building multi-agent AI systems on .NET — with parallel and sequential execution, MCP tool integration, real-time SSE streaming, multi-tenant auth with per-tenant rate limiting and cost budgets, evals/post-hoc scoring, and a React development playground UI.

[![CI](https://github.com/jigargajjarcad/orchestai/actions/workflows/ci.yml/badge.svg)](https://github.com/jigargajjarcad/orchestai/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Tests](https://img.shields.io/badge/tests-429%20passing-16a34a)](tests/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

---

## What It Does

Submit a task in natural language → the Orchestrator agent decomposes it → specialist agents run in parallel or sequential pipelines → results stream back to the UI in real time via SSE. Every request is authenticated to a tenant, rate-limited and budget-checked at admission time, and fully traceable afterward through timeline/summary/cost/error-rate endpoints and an eval/post-hoc-scoring layer.

```
User Task
   │
   ▼
OrchestratorAgent          ← decomposes task, selects agents, picks execution mode
   │
   ├── parallel ──────────── ResearchAgent ── firecrawl_scrape, perplexity_search
   │                    └── CodeAgent     ── file_write
   │
   └── sequential ─────────  ResearchAgent ──► WriterAgent (receives Research output)
                                               (prior output injected into prompt)
   │
   ▼
SSE Stream → React UI
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│  React UI (Vite + TailwindCSS) — dev/demo playground only   │
│  EventSource  ←  SSE stream        (see note below)         │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTP / SSE
┌────────────────────────▼────────────────────────────────────┐
│  ASP.NET Core API  (OrchestAI.API)                          │
│  TenantAuthenticationMiddleware → rate limiter → Controllers│
│  → MediatR                                                  │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  CQRS Layer  (OrchestAI.Application)                │   │
│  │  CreateOrchestrationTaskCommand                     │   │
│  │  AdmitOrchestrationTaskCommand ─► rate/budget checks│   │
│  │  StartOrchestrationCommand ─► parallel / sequential │   │
│  └──────────────────────┬──────────────────────────────┘   │
│                         │                                   │
│  ┌──────────────────────▼──────────────────────────────┐   │
│  │  Agent Layer  (OrchestAI.Infrastructure)            │   │
│  │  OrchestratorAgent → ResearchAgent / CodeAgent /    │   │
│  │  WriterAgent / DataAgent / BrowserAgent             │   │
│  │                                                     │   │
│  │  MCP Tools: FirecrawlTool · PerplexityTool ·        │   │
│  │             FileSystemTool                          │   │
│  └──────────────────────┬──────────────────────────────┘   │
│                         │                                   │
│  ┌──────────────────────▼──────────────────────────────┐   │
│  │  Data Layer  (EF Core 8 + PostgreSQL)               │   │
│  │  Tenant · ApiKey · TenantLimits · RejectionEvent    │   │
│  │  OrchestrationTask · AgentExecution · AgentMessage  │   │
│  │  McpToolCall · CostLedger · EvalSuite/Case/Run      │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### Frontend is an internal dev/demo playground, not a product

The React app under `frontend/` is a development and demo surface for exercising the API — it is
**not** a production-facing product frontend, and this is a confirmed product decision, not a
stopgap. Concretely:

- There is no self-serve tenant signup flow. Tenants and API keys are created out-of-band by an
  operator via the `/api/v1/admin/*` endpoints (see [Quick Start](#quick-start-local) below).
- There is no production-grade session auth. The frontend holds a pasted API key in an
  in-memory JS variable (`frontend/src/apiKey.js`) for the lifetime of the tab — never persisted
  to `localStorage`/`sessionStorage`, never sent as a query parameter, cleared on reload. This is
  deliberate for a local/demo tool, not a design awaiting a "real" auth mechanism; a production
  frontend would need a backend-for-frontend session, short-lived tokens, or httpOnly cookies.
- There is no current plan to make this frontend a public-facing product surface.

See `DECISIONS.md` ADR-014 Confirmation #11 for the full record of this decision.

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | C# .NET 8, ASP.NET Core |
| AI | Anthropic Claude API (claude-haiku-4-5) |
| Orchestration | CQRS with MediatR 12 |
| MCP Tools | Custom `IMcpTool` implementation |
| ORM | Entity Framework Core 8 + Npgsql |
| Database | PostgreSQL 15/16 |
| Streaming | Server-Sent Events (SSE) |
| Auth | Per-tenant API keys (`Authorization: Bearer`), admin bootstrap via static secret |
| Rate limiting | `Microsoft.AspNetCore.RateLimiting` token bucket, per-tenant |
| Frontend | React 19, Vite 8, react-markdown |
| Testing | xUnit, FluentAssertions, Moq |
| CI/CD | GitHub Actions (build/test, migration validation, container smoke test, security scan) |
| Deployment | Railway (API + DB) · Vercel (Frontend) |
| Container | Docker + Docker Compose |

---

## Quick Start (Local)

**Prerequisites:** Docker, .NET 8 SDK, Node 20+

Following this section end-to-end gets you a running API, a bootstrapped tenant + API key, and a
frontend that's actually able to authenticate against it — a bare `POST /tasks` with no API key
will 401.

Note on port: in the `Development` environment (the `dotnet run` default —
`src/OrchestAI.API/Properties/launchSettings.json` sets `ASPNETCORE_ENVIRONMENT=Development`),
`appsettings.Development.json`'s `Kestrel:Endpoints:Http:Url` pins the API to
**`http://localhost:5000`**, which wins over both the `PORT`-based fallback in `Program.cs` and
any `--urls` flag. The steps below use `5000` accordingly — don't substitute `8080` (that's only
the container/Railway default, from `Program.cs`'s `PORT ?? "8080"` fallback, which doesn't apply
here).

```bash
# 1. Clone
git clone https://github.com/your-username/orchestai.git
cd orchestai

# 2. Start PostgreSQL
docker compose up -d postgres

# 3. Configure and run the API
export Anthropic__ApiKey=sk-ant-...
export Admin__BootstrapSecret=<pick-any-long-random-string>   # gates the admin bootstrap endpoints below
export Tools__Firecrawl__ApiKey=fc-...   # optional
export Tools__Perplexity__ApiKey=pplx-... # optional
dotnet run --project src/OrchestAI.API
# API: http://localhost:5000  ·  Swagger: http://localhost:5000/swagger

# 4 & 5. Bootstrap a tenant + mint an API key (separate terminal). Either run the two admin
#    curl calls by hand, or use the bundled convenience script, which calls the exact same
#    admin-gated endpoints and just saves you the manual JSON parsing:
ADMIN_SECRET=<same-value-as-Admin__BootstrapSecret> ./scripts/bootstrap-local-dev.sh

# — or by hand: —
curl -s -X POST http://localhost:5000/api/v1/admin/tenants \
  -H "X-Admin-Secret: <same-value-as-Admin__BootstrapSecret>" \
  -H "Content-Type: application/json" \
  -d '{"name": "Local Dev", "slug": "local-dev"}'
# → { "tenantId": "...", "name": "Local Dev", "slug": "local-dev", "createdAt": "..." }

curl -s -X POST http://localhost:5000/api/v1/admin/api-keys \
  -H "X-Admin-Secret: <same-value-as-Admin__BootstrapSecret>" \
  -H "Content-Type: application/json" \
  -d '{"tenantId": "<tenantId-from-previous-step>", "displayName": "local-dev-key"}'
# → { "apiKeyId": "...", "rawKey": "<save-this-now>", "publicKeyId": "...", "createdAt": "..." }

# 6. Run the frontend (separate terminal)
export VITE_API_URL=http://localhost:5000
cd frontend && npm install && npm run dev
# UI: http://localhost:5173 — on first load it prompts for an API key; paste the rawKey from step 4/5
```

---

## Tenant & API-Key Model

Every request that isn't `/health/*`, `/swagger`, or `/api/v1/admin/*` is authenticated by
`TenantAuthenticationMiddleware` against an `Authorization: Bearer <key>` header, which resolves
to a `Tenant` and sets the ambient tenant scope for the rest of the request (data access is
tenant-scoped throughout — see ADR-014). Missing/malformed/unknown/revoked key → `401`; valid key
on a suspended tenant → `403`.

Tenants and keys are never self-service — they're created by an operator through the admin
surface, gated by a static `X-Admin-Secret` header (must equal the `Admin:BootstrapSecret`
config value / `Admin__BootstrapSecret` env var; the endpoints return `503` if that value isn't
configured at all):

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/v1/admin/tenants` | Create a tenant (`name`, `slug`) |
| `POST` | `/api/v1/admin/api-keys` | Mint an API key for a tenant (`tenantId`, `displayName`) — raw key returned once |
| `POST` | `/api/v1/admin/api-keys/{apiKeyId}/revoke` | Revoke a key |
| `POST` | `/api/v1/admin/tenants/{tenantId}/suspend` | Suspend a tenant (blocks all its keys with `403`) |
| `PUT` | `/api/v1/admin/tenants/{tenantId}/limits` | Set a tenant's rate/concurrency/cost limits |

See [Quick Start](#quick-start-local) above for a concrete bootstrap example.

---

## Rate Limiting & Abuse/Cost Protection

Every tenant gets a token-bucket rate limit plus a set of structural and cost ceilings, checked
at admission time before any agent work is dispatched. Defaults (overridable per-tenant via
`PUT /api/v1/admin/tenants/{tenantId}/limits`):

| Limit | Default | Enforced by |
|---|---|---|
| `RequestsPerMinute` | 120 | Token-bucket rate limiter (`Microsoft.AspNetCore.RateLimiting`), partitioned per tenant |
| `MaxConcurrentTasks` | 5 | Admission check (`IOrchestrationAdmissionRepository.TryAdmitAsync`) in `AdmitOrchestrationTaskCommand` |
| `MaxAgentsPerTask` | 5 | Checked once, pre-dispatch, against the Orchestrator's plan |
| `MaxToolCallsPerTask` | 50 | Running counter checked per tool call across all of a task's agents |
| `DailyCostBudgetUsd` | $50 | Admission check against `CostLedger`/`CostRollup` |
| `MonthlyCostBudgetUsd` | $500 | Admission check against `CostLedger`/`CostRollup` |
| `MaxQueueDepth` | 100 | Per-tenant eval-run queue depth check (`IEvalRunQueue`) — not applied to task admission |

The rate limiter applies to every `/api/v1/*` route except `/health/*`, `/swagger`,
`/api/v1/admin/*`, and any path ending `/stream` (long-lived SSE connections don't fit
request-rate semantics). All rejections — rate limit, concurrency, budget, agent-cap, or queue —
return a single unified `429` shape:

```json
{
  "type": "https://orchestai/problems/rate-limited",
  "title": "Too Many Requests",
  "status": 429,
  "detail": "Rate limit exceeded.",
  "reason": "RateLimited",
  "retryAfterSeconds": 42
}
```

and are persisted as a `RejectionEvent`, queryable via `GET /api/v1/rejections?limit=50`
(recent rate-limit/concurrency/budget/agent-cap/queue rejections for the caller's tenant).

This enforcement state is in-process memory only (not Redis-backed) — see ADR-015 Confirmation #2's
"In-memory state is an accepted limitation" confirmation for the single-instance-deployment caveat.

---

## Observability

Per-task tracing:

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/v1/tasks/{id}/timeline` | Chronological trace tree — agent executions + tool calls |
| `GET` | `/api/v1/tasks/{id}/summary` | At-a-glance card — status, cost, agents, retries, errors, memory/checkpoint use |
| `GET` | `/api/v1/tasks/compare?firstTaskId=...&secondTaskId=...` | Side-by-side comparison of two runs — prompts, outputs, latency, cost, tokens |

Per-user rollups:

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/v1/users/{userId}/observability/cost-dashboard?from=YYYY-MM-DD&to=YYYY-MM-DD` | Cost breakdown by day/agent/model |
| `GET` | `/api/v1/users/{userId}/observability/error-rates?from=YYYY-MM-DD&to=YYYY-MM-DD` | Agent/tool failure rates over time, by failure reason, with retry counts |
| `GET` | `/api/v1/users/{userId}/tasks?limit=20` | Most recent tasks for a user — powers the observability task picker |

Both dashboard endpoints return `400` if `to` is earlier than `from`.

---

## Evals & Post-Hoc Scoring

All under `/api/v1/eval-suites` (and two routes rooted at `/api/v1/eval-runs` /
`/api/v1/post-hoc-scoring`), tenant-scoped like everything else:

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/api/v1/eval-suites` | Create an eval suite targeting one agent type |
| `GET` | `/api/v1/eval-suites` | List eval suites |
| `POST` | `/api/v1/eval-suites/{suiteId}/cases` | Add a test case to a suite |
| `POST` | `/api/v1/eval-suites/{suiteId}/runs` | Trigger a run, optionally against a baseline run |
| `GET` | `/api/v1/eval-suites/{suiteId}/runs` | List runs for a suite (baseline-run picker) |
| `GET` | `/api/v1/eval-runs/{runId}/results` | Per-case results for one run |
| `GET` | `/api/v1/eval-runs/{runId}/regression-report` | Diff a run against its baseline (`400` if none set) |
| `POST` | `/api/v1/post-hoc-scoring` | Score historical `AgentExecution` traces judge-only, no re-execution |
| `GET` | `/api/v1/eval-runs/{runId}/posthoc-summary` | Pass rate / score distribution for a post-hoc scoring run |

Post-hoc scoring lets you retroactively judge real production traces (by date range, agent type,
or explicit trace IDs) against a rubric, without re-running the original task.

---

## Agent Capability Matrix

| Agent | Model | Tools | Best For |
|---|---|---|---|
| **Orchestrator** | claude-haiku-4-5 | — | Task decomposition, mode selection |
| **Research** | claude-haiku-4-5 | `perplexity_search`, `firecrawl_scrape` | Web research, citations |
| **Writer** | claude-haiku-4-5 | `file_write`, `firecrawl_scrape` | Reports, summaries, formatting |
| **Code** | claude-haiku-4-5 | `file_write` | Code generation, file output |
| **Data** | claude-haiku-4-5 | `firecrawl_scrape` | Structured data extraction |
| **Browser** | claude-haiku-4-5 | — | Browser automation (extensible) |

---

## API Reference

All endpoints are prefixed `/api/v1` unless noted. Every endpoint below except `/api/v1/admin/*`
requires `Authorization: Bearer <key>` (see [Tenant & API-Key Model](#tenant--api-key-model)).

### Create Task
```
POST /tasks
Authorization: Bearer <key>
Content-Type: application/json

{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Research .NET AI ecosystem",
  "userPrompt": "Research the latest developments in .NET AI libraries in 2025..."
}
```
Supports an optional `Idempotency-Key` header.

### Start Orchestration
```
POST /tasks/{taskId}/start
```
Admits the task (rate/concurrency/budget checks — synchronous, may `429`/`404`/`409`) and
dispatches agent execution in the background. Returns `202`.

### Resume Orchestration
```
POST /tasks/{taskId}/resume
```
Resumes a `Failed` task from its first agent without a saved checkpoint. Returns `202`.

### Approve / Reject (human-in-the-loop review)
```
POST /tasks/{taskId}/approve
POST /tasks/{taskId}/reject
```
Body: `{ "note": "optional reviewer note" }`.

### Stream Events (SSE)
```
POST /tasks/{taskId}/stream-ticket   → { "ticket": "...", "expiresInSeconds": 60 }
GET  /tasks/{taskId}/stream?ticket=<ticket>
```
Browser `EventSource` cannot send an `Authorization` header, so the SSE endpoint is exempted from
tenant-auth middleware and instead requires a short-lived (60s), single-use, task-bound ticket
minted via `POST .../stream-ticket` (which itself is a normal Bearer-authenticated call). Returns
a stream of `text/event-stream` events — see [SSE Events](#sse-events) below.

### Get Task
```
GET /tasks/{taskId}?includeMessages=true&includeToolCalls=true
```

### Health Checks
```
GET /health/live
→ 200 { "status": "alive", "timestamp": "..." }
```
Liveness only — the process is up, independent of the database. Never gates traffic routing.

```
GET /health/ready
→ 200 { "status": "ready", "timestamp": "..." }
→ 503 { "status": "not_ready", "reason": "...", "timestamp": "..." }
```
Readiness — `200` only if the database is reachable and there are no pending EF Core migrations.
This is what `railway.json`'s `healthcheckPath` actually points at.

Both are unauthenticated and exempt from tenant auth and rate limiting.

### SSE Events

| Event | Payload |
|---|---|
| `task_started` | `{ taskId, status }` |
| `orchestrator_plan` | `{ plan, executionMode, selectedAgents, executionOrder, agentPrompts }` |
| `agent_started` | `{ agentExecutionId, agentType }` |
| `message_written` | `{ agentExecutionId, agentType, contentPreview }` |
| `tool_started` | `{ agentExecutionId, toolName }` |
| `tool_completed` | `{ agentExecutionId, toolName, success, durationMs, outputPreview }` |
| `agent_completed` | `{ agentExecutionId, inputTokens, outputTokens, costUsd }` |
| `agent_failed` | `{ agentExecutionId, errorMessage }` |
| `task_completed` | `{ taskId, totalCostUsd, agentCount }` |
| `task_failed` | `{ taskId, errorMessage }` |

---

## Sequential vs Parallel Execution

The Orchestrator selects the execution mode based on the task:

**Parallel** — agents run concurrently via `Task.WhenAll`:
```json
{ "execution_mode": "parallel", "agents": ["Research", "Data"] }
```

**Sequential** — agents run one at a time, each receiving the prior agent's output injected into its prompt (up to 3,000 characters):
```json
{ "execution_mode": "sequential", "execution_order": ["Research", "Writer"] }
```

Sequential failure policy: if one agent fails, execution continues to the next agent without prior context injection. The task is marked failed at the end if any agent failed.

---

## CI/CD Pipeline

`.github/workflows/ci.yml` runs four jobs on every push to `main` (and on-demand via
`workflow_dispatch`):

| Job | Purpose |
|---|---|
| `build-and-test` | Restore/build/apply migrations against a fresh Postgres, run the full test suite |
| `migration-validation` | Verifies a fresh-install schema and an upgrade-path schema (prior migration → latest) converge byte-for-byte via `pg_dump` diff, and re-runs the full test suite against the upgraded database |
| `container-smoke-test` | Builds the real production `Dockerfile`, runs the container, polls `/health/ready` until healthy, checks `/health/live` independently |
| `security-scan` | `dotnet list package --vulnerable` (fails on High/Critical) and a Trivy container scan (fails on fixable High/Critical CVEs) |

---

## Deployment

### Railway (API + PostgreSQL)

1. Create a new Railway project and add a PostgreSQL plugin
2. Connect your GitHub repo, set root directory to `.` (uses `Dockerfile` at root)
3. Set environment variables in Railway dashboard:

```
Anthropic__ApiKey=sk-ant-...
Admin__BootstrapSecret=<long-random-string>
Tools__Firecrawl__ApiKey=fc-...
Tools__Perplexity__ApiKey=pplx-...
ALLOWED_ORIGINS=https://your-app.vercel.app
ConnectionStrings__DefaultConnection=<Railway PostgreSQL URL in Npgsql format>
```

EF Core migrations run automatically on startup. `railway.json`'s `healthcheckPath` targets
`/health/ready`.

### Vercel (Frontend)

1. Import the `frontend/` directory (or the full repo and set root to `frontend`)
2. Framework: Vite
3. Set environment variable:

```
VITE_API_URL=https://your-api.railway.app
```

The `frontend/vercel.json` configures SPA routing. The frontend calls the Railway API directly —
no proxy needed. Remember this frontend is a dev/demo playground (see
[above](#frontend-is-an-internal-devdemo-playground-not-a-product)) — deploying it publicly still
requires pasting a real tenant API key into the browser prompt on each visit.

---

## Project Structure

```
orchestai/
├── src/
│   ├── OrchestAI.API/           ← ASP.NET Core controllers, SSE, Program.cs
│   │   └── Controllers/         ← Tasks, Admin, Evals, Observability, Rejections, Memories
│   ├── OrchestAI.Application/   ← CQRS commands, queries, MediatR handlers
│   ├── OrchestAI.Domain/        ← Entities, interfaces, enums, domain models
│   └── OrchestAI.Infrastructure/← Agents, MCP tools, EF Core, repositories
├── tests/
│   └── OrchestAI.Tests/         ← 429 xUnit tests (unit + integration)
├── frontend/                    ← React 19 + Vite dev/demo playground UI
├── .github/workflows/ci.yml     ← build-and-test, migration-validation, container-smoke-test, security-scan
├── Dockerfile                   ← Multi-stage .NET 8 build
├── docker-compose.yml           ← PostgreSQL for local dev
├── railway.json                 ← Railway deployment config
├── DECISIONS.md                 ← Architecture Decision Records
└── ARCHITECTURE.md              ← Deep technical reference
```

---

## Running Tests

```bash
dotnet test tests/OrchestAI.Tests
# Passed! - Failed: 0, Passed: 429, Skipped: 0
```

---

## License

MIT
