# OrchestAI

> Production-ready multi-agent AI orchestration for .NET 8

**The .NET alternative to LangGraph, AutoGen, and CrewAI.** OrchestAI provides a complete CQRS-based framework for building multi-agent AI systems on .NET — with parallel and sequential execution, MCP tool integration, real-time SSE streaming, and a React playground UI.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![Tests](https://img.shields.io/badge/tests-182%20passing-16a34a)](tests/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

---

## What It Does

Submit a task in natural language → the Orchestrator agent decomposes it → specialist agents run in parallel or sequential pipelines → results stream back to the UI in real time via SSE.

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
│  React UI (Vite + TailwindCSS)                              │
│  EventSource  ←  SSE stream                                 │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTP / SSE
┌────────────────────────▼────────────────────────────────────┐
│  ASP.NET Core API  (OrchestAI.API)                          │
│  TasksController → MediatR                                   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  CQRS Layer  (OrchestAI.Application)                │   │
│  │  CreateOrchestrationTaskCommand                     │   │
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
│  │  OrchestrationTask · AgentExecution · AgentMessage  │   │
│  │  McpToolCall · CostLedger                           │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | C# .NET 8, ASP.NET Core |
| AI | Anthropic Claude API (claude-haiku-4-5) |
| Orchestration | CQRS with MediatR 12 |
| MCP Tools | Custom `IMcpTool` implementation |
| ORM | Entity Framework Core 8 + Npgsql |
| Database | PostgreSQL 15 |
| Streaming | Server-Sent Events (SSE) |
| Frontend | React 19, Vite 8, react-markdown |
| Testing | xUnit, FluentAssertions, Moq |
| Deployment | Railway (API + DB) · Vercel (Frontend) |
| Container | Docker + Docker Compose |

---

## Quick Start (Local)

**Prerequisites:** Docker, .NET 8 SDK, Node 20+

```bash
# 1. Clone
git clone https://github.com/your-username/orchestai.git
cd orchestai

# 2. Start PostgreSQL
docker compose up -d postgres

# 3. Run the API
export Anthropic__ApiKey=sk-ant-...
export Tools__Firecrawl__ApiKey=fc-...   # optional
export Tools__Perplexity__ApiKey=pplx-... # optional
dotnet run --project src/OrchestAI.API

# 4. Run the frontend (separate terminal)
cd frontend && npm install && npm run dev

# UI: http://localhost:5173
# API: http://localhost:8080
# Swagger: http://localhost:8080/swagger
```

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

All endpoints are prefixed `/api/v1`.

### Create Task
```
POST /tasks
Content-Type: application/json

{
  "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "title": "Research .NET AI ecosystem",
  "userPrompt": "Research the latest developments in .NET AI libraries in 2025..."
}
```

### Start Orchestration
```
POST /tasks/{taskId}/start
```

### Stream Events (SSE)
```
GET /tasks/{taskId}/stream
```
Returns a stream of `text/event-stream` events. See [SSE Events](#sse-events) below.

### Get Task
```
GET /tasks/{taskId}?includeMessages=true&includeToolCalls=true
```

### Health Check
```
GET /health
→ { "status": "healthy", "timestamp": "..." }
```

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

## Deployment

### Railway (API + PostgreSQL)

1. Create a new Railway project and add a PostgreSQL plugin
2. Connect your GitHub repo, set root directory to `.` (uses `Dockerfile` at root)
3. Set environment variables in Railway dashboard:

```
Anthropic__ApiKey=sk-ant-...
Tools__Firecrawl__ApiKey=fc-...
Tools__Perplexity__ApiKey=pplx-...
ALLOWED_ORIGINS=https://your-app.vercel.app
ConnectionStrings__DefaultConnection=<Railway PostgreSQL URL in Npgsql format>
```

EF Core migrations run automatically on startup.

### Vercel (Frontend)

1. Import the `frontend/` directory (or the full repo and set root to `frontend`)
2. Framework: Vite
3. Set environment variable:

```
VITE_API_URL=https://your-api.railway.app
```

The `frontend/vercel.json` configures SPA routing. The frontend calls the Railway API directly — no proxy needed.

---

## Project Structure

```
orchestai/
├── src/
│   ├── OrchestAI.API/           ← ASP.NET Core controllers, SSE, Program.cs
│   ├── OrchestAI.Application/   ← CQRS commands, queries, MediatR handlers
│   ├── OrchestAI.Domain/        ← Entities, interfaces, enums, domain models
│   └── OrchestAI.Infrastructure/← Agents, MCP tools, EF Core, repositories
├── tests/
│   └── OrchestAI.Tests/         ← 59 xUnit tests (unit + integration)
├── frontend/                    ← React 19 + Vite playground UI
├── Dockerfile                   ← Multi-stage .NET 8 build
├── docker-compose.yml           ← PostgreSQL for local dev
├── railway.json                 ← Railway deployment config
└── ARCHITECTURE.md              ← Deep technical reference
```

---

## Running Tests

```bash
dotnet test
# Passed! - Failed: 0, Passed: 59, Skipped: 0
```

---

## License

MIT
