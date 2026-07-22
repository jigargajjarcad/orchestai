# OrchestAI — Product Requirements Document

## 1. Project Overview

### Problem Statement
The Python ecosystem has mature multi-agent orchestration frameworks: LangGraph, AutoGen, CrewAI. The .NET ecosystem has nothing production-grade. Teams building enterprise AI systems in C# are forced to either hand-roll orchestration logic or adopt Python dependencies that conflict with their existing architecture.

### What OrchestAI Is
A production-ready C# .NET 8 boilerplate for multi-agent AI orchestration. It provides:
- A complete CQRS-based architecture for dispatching agent tasks
- Parallel sub-agent execution (Research, Code, Data specialists)
- MCP (Model Context Protocol) tool integration
- Server-Sent Events (SSE) for real-time streaming output
- React frontend playground for testing agent runs
- Full Docker Compose local dev environment
- Railway + Vercel deployment-ready

### Who This Is For
Enterprise .NET development teams building AI applications. Organizations already
invested in C#/.NET who need production-grade multi-agent orchestration without
adopting a Python toolchain or hand-rolling their own framework. This is the single
target customer — every roadmap and positioning decision downstream is filtered
through "does this serve an enterprise .NET team," not general developer adoption
or hobbyist use.

### Success Criteria
- Developer can clone, configure API key, and run a multi-agent task in under 5 minutes
- All three specialized agents (Research, Code, Data) execute in parallel
- Real-time streaming shows agent progress in the React UI as it happens
- Full session replay from database — every tool call logged with cost
- GitHub stars and forks as portfolio visibility metric

### Key Differentiator
First open-source, production-grade, multi-agent CQRS framework for .NET 8. Not a toy. Not a proof of concept. Deployable, observable, and extensible.

---

## 2. Current State (Week 4 Complete)

### What's Built and Deployed
Weeks 1–4 are complete: full backend + frontend, deployed and live.
- CQRS backend on .NET 8 with 6 domain entities, EF Core migrations auto-applied on startup
- 6 agents (Orchestrator + 5 specialists) with a shared agentic tool-call loop
- 3 MCP tools wired to real external APIs (not stubs)
- Parallel **and** sequential agent execution, selected by the Orchestrator per task
- Full SSE event stream (10 event types) consumed live by the React UI
- 59 xUnit tests passing (unit + integration), CI-ready
- Dockerized locally; deployed to Railway (API + Postgres) and Vercel (frontend)

### Live URLs
| Service | URL |
|---|---|
| API (Railway) | https://orchestai-production.up.railway.app |
| Frontend (Vercel) | https://orchestai-git-main-jigar-gajjar.vercel.app |
| Health check | https://orchestai-production.up.railway.app/health |
| Swagger | https://orchestai-production.up.railway.app/swagger |

### Tech Stack (as shipped)
| Layer | Technology |
|---|---|
| Backend | C# .NET 8, ASP.NET Core |
| AI | Anthropic Claude API (claude-haiku-4-5) |
| Orchestration | CQRS with MediatR 12.4.1 (MIT-licensed; 13+ requires a commercial license) |
| MCP Tools | Custom `IMcpTool` implementation |
| ORM | Entity Framework Core 8 + Npgsql |
| Database | PostgreSQL |
| Streaming | Server-Sent Events (SSE), direct — no proxy buffering |
| Frontend | React 19, Vite 8, react-markdown |
| Testing | xUnit, FluentAssertions, Moq |
| Deployment | Railway (API + DB) · Vercel (Frontend) |
| Container | Docker + Docker Compose |

### Agents (6)
`OrchestratorAgent` (task decomposition + execution-mode selection) delegating to five specialists: `ResearchAgent` (`firecrawl_scrape`, `perplexity_search`), `WriterAgent` (`file_write`, `firecrawl_scrape`), `CodeAgent` (`file_write`), `DataAgent` (`firecrawl_scrape`), `BrowserAgent` (extensible, no tools bound yet).

### Tools (3)
`FirecrawlTool` (web scraping), `PerplexityTool` (web search with citations), `FileSystemTool` (sandboxed file writes under `./agent-workspace/`).

### Execution Modes
The Orchestrator chooses per task: **parallel** (`Task.WhenAll` across selected agents) or **sequential** (agents run one at a time, each receiving the prior agent's output injected into its prompt, truncated to 3,000 characters — see ADR-002 in `DECISIONS.md`).

### Tests
59 tests passing across `StartOrchestrationHandlerTests`, `OrchestratorAgentTests`, `FileSystemToolTests`, `FirecrawlToolTests`, `PerplexityToolTests`, `AgentBaseToolLoopTests`, `CreateOrchestrationTaskHandlerTests`.

> Sections 4–16 below capture the original Week 1 design intent. Implementation details (entity names, tool names, agent count) evolved during the build — `README.md` and `ARCHITECTURE.md` are the authoritative reference for exact current schema and API shape.

---

## 3. Competitive Positioning

### vs. LangGraph
Same core patterns — stateful agent graphs, tool-calling loops, conditional routing between parallel and sequential execution — but native .NET. No Python interop, no subprocess bridge, no separate runtime to operate. Drops into existing enterprise .NET stacks using the same CQRS/MediatR/EF Core patterns .NET teams already run in production, with compile-time type safety on agent and tool contracts (`IAgent`, `IMcpTool`) instead of loosely-typed Python dicts.

### vs. CrewAI
Same agent-collaboration model — an orchestrator delegating to role-specialized agents that can run in parallel or as a pipeline — but built on enterprise-grade architecture instead of a lightweight scripting framework: CQRS command/query separation, structured logging on every agent operation, a persisted cost ledger per token/agent/session, a typed exception hierarchy with error codes, and a real relational schema (not in-memory state) backing every run.

### vs. n8n
Code-first, not visual-first. Every agent, tool, and execution path is version-controlled, unit-tested (59 tests), and extensible by implementing an interface — not by dragging nodes on a canvas. Built for autonomous multi-step reasoning (an agentic tool-call loop that decides its own next action) rather than a fixed, human-authored workflow graph.

---

## 4. System Architecture

### High-Level Architecture
```
React Frontend (Vercel)
        ↓ HTTP + SSE
ASP.NET Core API (Railway)
        ↓ CQRS / MediatR
Orchestrator Agent
    ↓           ↓           ↓
Research     Code        Data
Agent        Agent       Agent
    ↓           ↓           ↓
         MCP Tool Layer
    WebSearch  FileSystem  Database
        ↓
PostgreSQL (Railway)
```

### Agent Communication Pattern
```
User Task
    ↓
RunOrchestratorCommand → MediatR → OrchestratorHandler
    ↓
Orchestrator analyzes task → creates SubTaskPlan
    ↓ (parallel execution)
ResearchAgent.ExecuteAsync()  →  WebSearch MCP Tool
CodeAgent.ExecuteAsync()      →  FileSystem MCP Tool
DataAgent.ExecuteAsync()      →  Database MCP Tool
    ↓ (aggregation)
Orchestrator.AggregateResults()
    ↓
Stream final response via SSE → React UI
```

### Tech Stack
| Layer | Technology | Responsibility |
|---|---|---|
| API | ASP.NET Core 8 | HTTP endpoints, SSE streaming, request validation |
| Application | MediatR 12 + CQRS | Commands, queries, handlers, DTOs, pipeline behaviors |
| Domain | C# class library | Entities, interfaces, enums, domain exceptions |
| Infrastructure | EF Core + Anthropic SDK | Agent implementations, MCP tools, DB repositories |
| Frontend | React + TailwindCSS | Playground UI, SSE consumer, AgentCard components |
| Database | PostgreSQL 15 | Sessions, sub-tasks, tool calls, messages, costs |
| Streaming | Server-Sent Events | Real-time agent output to browser |
| Container | Docker Compose | Local dev: API + DB + frontend |
| Deployment | Railway + Vercel | API/DB on Railway, frontend on Vercel |

### Agent Topology
```
OrchestratorAgent (coordinator)
├── ResearchAgent    (specialist — web search, summarization)
├── CodeAgent        (specialist — code generation, file operations)
└── DataAgent        (specialist — data analysis, DB queries)
```

Each agent is a Claude claude-sonnet-4-6 call with a distinct system prompt and a registered set of MCP tools. The Orchestrator routes work by decomposing the user task into typed SubTasks, each carrying an `AgentType` enum value that determines which specialist handles it.

---

## 5. Database Schema

All tables use `uuid` primary keys and `timestamptz` timestamps for correct timezone handling.

### agent_sessions
Represents one user-initiated orchestration run.
```sql
CREATE TABLE agent_sessions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    task            TEXT NOT NULL,
    status          VARCHAR(50) NOT NULL DEFAULT 'pending',
    -- pending | running | completed | failed
    result          TEXT,
    error_message   TEXT,
    total_tokens    INTEGER NOT NULL DEFAULT 0,
    total_cost_usd  DECIMAL(10, 6) NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    completed_at    TIMESTAMPTZ
);
```

### sub_tasks
One row per specialist agent invocation within a session.
```sql
CREATE TABLE sub_tasks (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id       UUID NOT NULL REFERENCES agent_sessions(id) ON DELETE CASCADE,
    agent_type       VARCHAR(50) NOT NULL,
    -- orchestrator | research | code | data
    task_description TEXT NOT NULL,
    status           VARCHAR(50) NOT NULL DEFAULT 'pending',
    -- pending | running | completed | failed
    result           TEXT,
    error_message    TEXT,
    started_at       TIMESTAMPTZ,
    completed_at     TIMESTAMPTZ,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_sub_tasks_session_id ON sub_tasks(session_id);
```

### tool_calls
Audit log for every MCP tool invocation.
```sql
CREATE TABLE tool_calls (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    sub_task_id  UUID NOT NULL REFERENCES sub_tasks(id) ON DELETE CASCADE,
    tool_name    VARCHAR(100) NOT NULL,
    input_json   JSONB NOT NULL,
    output_json  JSONB,
    status       VARCHAR(50) NOT NULL DEFAULT 'pending',
    -- pending | success | error
    error_message TEXT,
    duration_ms  INTEGER,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_tool_calls_sub_task_id ON tool_calls(sub_task_id);
```

### agent_messages
Full message history for every agent conversation (system + user + assistant turns).
```sql
CREATE TABLE agent_messages (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id  UUID REFERENCES agent_sessions(id) ON DELETE CASCADE,
    sub_task_id UUID REFERENCES sub_tasks(id) ON DELETE SET NULL,
    role        VARCHAR(20) NOT NULL,
    -- system | user | assistant | tool_result
    content     TEXT NOT NULL,
    token_count INTEGER,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_agent_messages_session_id ON agent_messages(session_id);
CREATE INDEX idx_agent_messages_sub_task_id ON agent_messages(sub_task_id);
```

### session_costs
Token and cost breakdown per sub-task for observability.
```sql
CREATE TABLE session_costs (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id         UUID NOT NULL REFERENCES agent_sessions(id) ON DELETE CASCADE,
    sub_task_id        UUID REFERENCES sub_tasks(id) ON DELETE SET NULL,
    agent_type         VARCHAR(100),
    input_tokens       INTEGER NOT NULL DEFAULT 0,
    output_tokens      INTEGER NOT NULL DEFAULT 0,
    cache_read_tokens  INTEGER NOT NULL DEFAULT 0,
    cache_write_tokens INTEGER NOT NULL DEFAULT 0,
    cost_usd           DECIMAL(10, 6) NOT NULL DEFAULT 0,
    model_used         VARCHAR(100) NOT NULL,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_session_costs_session_id ON session_costs(session_id);
```

### mcp_tools
Registry of available MCP tools and their metadata.
```sql
CREATE TABLE mcp_tools (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name         VARCHAR(100) NOT NULL UNIQUE,
    description  TEXT,
    input_schema JSONB,
    is_enabled   BOOLEAN NOT NULL DEFAULT TRUE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

---

## 6. Project Structure

```
orchestai/
├── PRD.md
├── CLAUDE.md
├── README.md
├── .gitignore
├── docker-compose.yml
├── docker-compose.override.yml        # local dev secrets
│
├── .github/
│   └── workflows/
│       └── ci.yml
│
├── backend/
│   ├── OrchestAI.sln
│   │
│   ├── src/
│   │   ├── OrchestAI.API/
│   │   │   ├── OrchestAI.API.csproj
│   │   │   ├── Program.cs
│   │   │   ├── appsettings.json
│   │   │   ├── appsettings.Development.json
│   │   │   ├── Controllers/
│   │   │   │   ├── AgentsController.cs
│   │   │   │   ├── SessionsController.cs
│   │   │   │   └── AdminController.cs
│   │   │   ├── Middleware/
│   │   │   │   ├── ExceptionHandlingMiddleware.cs
│   │   │   │   └── RequestLoggingMiddleware.cs
│   │   │   └── Extensions/
│   │   │       ├── ServiceCollectionExtensions.cs
│   │   │       └── WebApplicationExtensions.cs
│   │   │
│   │   ├── OrchestAI.Application/
│   │   │   ├── OrchestAI.Application.csproj
│   │   │   ├── Commands/
│   │   │   │   └── RunOrchestrator/
│   │   │   │       ├── RunOrchestratorCommand.cs
│   │   │   │       ├── RunOrchestratorCommandHandler.cs
│   │   │   │       └── RunOrchestratorCommandValidator.cs
│   │   │   ├── Queries/
│   │   │   │   ├── GetSession/
│   │   │   │   │   ├── GetSessionQuery.cs
│   │   │   │   │   └── GetSessionQueryHandler.cs
│   │   │   │   └── GetAllSessions/
│   │   │   │       ├── GetAllSessionsQuery.cs
│   │   │   │       └── GetAllSessionsQueryHandler.cs
│   │   │   ├── DTOs/
│   │   │   │   ├── SessionDetailDto.cs
│   │   │   │   ├── SessionSummaryDto.cs
│   │   │   │   ├── SubTaskDto.cs
│   │   │   │   ├── ToolCallDto.cs
│   │   │   │   └── SessionCostDto.cs
│   │   │   ├── Behaviors/
│   │   │   │   ├── LoggingBehavior.cs
│   │   │   │   └── ValidationBehavior.cs
│   │   │   └── Common/
│   │   │       └── PagedResult.cs
│   │   │
│   │   ├── OrchestAI.Domain/
│   │   │   ├── OrchestAI.Domain.csproj
│   │   │   ├── Entities/
│   │   │   │   ├── AgentSession.cs
│   │   │   │   ├── SubTask.cs
│   │   │   │   ├── ToolCall.cs
│   │   │   │   ├── AgentMessage.cs
│   │   │   │   ├── SessionCost.cs
│   │   │   │   └── McpToolDefinition.cs
│   │   │   ├── Enums/
│   │   │   │   ├── AgentType.cs
│   │   │   │   ├── SessionStatus.cs
│   │   │   │   └── SubTaskStatus.cs
│   │   │   ├── Interfaces/
│   │   │   │   ├── IAgent.cs
│   │   │   │   ├── IOrchestrator.cs
│   │   │   │   ├── IMcpTool.cs
│   │   │   │   ├── IMcpToolRegistry.cs
│   │   │   │   ├── IAgentSessionRepository.cs
│   │   │   │   └── ISubTaskRepository.cs
│   │   │   └── Exceptions/
│   │   │       ├── OrchestAIException.cs
│   │   │       ├── AgentExecutionException.cs
│   │   │       ├── SessionNotFoundException.cs
│   │   │       └── McpToolException.cs
│   │   │
│   │   └── OrchestAI.Infrastructure/
│   │       ├── OrchestAI.Infrastructure.csproj
│   │       ├── AI/
│   │       │   ├── AnthropicService.cs
│   │       │   └── CostCalculator.cs
│   │       ├── Agents/
│   │       │   ├── BaseAgent.cs
│   │       │   ├── OrchestratorAgent.cs
│   │       │   ├── ResearchAgent.cs
│   │       │   ├── CodeAgent.cs
│   │       │   └── DataAgent.cs
│   │       ├── Mcp/
│   │       │   ├── McpToolRegistry.cs
│   │       │   ├── McpExecutor.cs
│   │       │   └── Tools/
│   │       │       ├── WebSearchTool.cs
│   │       │       ├── FileSystemTool.cs
│   │       │       └── DatabaseTool.cs
│   │       ├── Persistence/
│   │       │   ├── OrchestAIDbContext.cs
│   │       │   ├── Configurations/
│   │       │   │   ├── AgentSessionConfiguration.cs
│   │       │   │   ├── SubTaskConfiguration.cs
│   │       │   │   ├── ToolCallConfiguration.cs
│   │       │   │   ├── AgentMessageConfiguration.cs
│   │       │   │   ├── SessionCostConfiguration.cs
│   │       │   │   └── McpToolDefinitionConfiguration.cs
│   │       │   ├── Repositories/
│   │       │   │   ├── AgentSessionRepository.cs
│   │       │   │   ├── SubTaskRepository.cs
│   │       │   │   └── ToolCallRepository.cs
│   │       │   └── Migrations/
│   │       ├── Streaming/
│   │       │   └── SseStreamService.cs
│   │       └── Extensions/
│   │           └── InfrastructureServiceExtensions.cs
│   │
│   └── tests/
│       └── OrchestAI.Tests/
│           ├── OrchestAI.Tests.csproj
│           ├── Unit/
│           │   ├── OrchestratorAgentTests.cs
│           │   ├── RunOrchestratorCommandHandlerTests.cs
│           │   ├── GetSessionQueryHandlerTests.cs
│           │   └── McpToolTests.cs
│           └── Integration/
│               └── AgentSessionTests.cs
│
└── frontend/
    ├── package.json
    ├── tsconfig.json
    ├── tailwind.config.js
    ├── vite.config.ts
    ├── index.html
    ├── vercel.json
    └── src/
        ├── main.tsx
        ├── App.tsx
        ├── pages/
        │   ├── Playground.tsx
        │   ├── Sessions.tsx
        │   ├── SessionDetail.tsx
        │   └── Admin.tsx
        ├── components/
        │   ├── TaskInput.tsx
        │   ├── AgentStatusGrid.tsx
        │   ├── AgentCard.tsx
        │   ├── AgentStatusBadge.tsx
        │   ├── ToolCallLog.tsx
        │   ├── StreamOutput.tsx
        │   └── SessionCard.tsx
        ├── hooks/
        │   ├── useAgentStream.ts
        │   └── useSessions.ts
        ├── services/
        │   ├── api.ts
        │   └── agentService.ts
        └── types/
            ├── session.ts
            └── stream.ts
```

---

## 7. API Design

### Base URL
- Local: `http://localhost:5000/api/v1`
- Production: `https://orchestai-api.up.railway.app/api/v1`

### Error Response Standard
```json
{
  "error": {
    "code": "SESSION_NOT_FOUND",
    "message": "Agent session with id xyz does not exist"
  }
}
```

### Agent Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/agents/run` | Submit a task to the orchestrator |
| GET | `/agents/sessions` | List all sessions (paginated) |
| GET | `/agents/sessions/{id}` | Get session with full detail |
| GET | `/agents/sessions/{id}/stream` | SSE stream for live session |
| DELETE | `/agents/sessions/{id}` | Delete a session |

#### POST /api/v1/agents/run
Starts an orchestration run. Returns a `sessionId` for SSE subscription.
```json
// Request
{
  "task": "Research the latest .NET 8 performance improvements and write a summary with code examples",
  "config": {
    "maxParallelAgents": 3,
    "timeoutSeconds": 120
  }
}

// Response 202 Accepted
{
  "sessionId": "uuid",
  "status": "running",
  "streamUrl": "/api/v1/agents/sessions/uuid/stream"
}
```

#### GET /api/v1/agents/sessions/{id}/stream
SSE endpoint. `Content-Type: text/event-stream`. Streams events until session completes.
```
event: agent_started
data: {"agent": "ResearchAgent", "task": "..."}

event: tool_call
data: {"agent": "ResearchAgent", "tool": "WebSearchTool", "input": {...}}

event: tool_result
data: {"agent": "ResearchAgent", "tool": "WebSearchTool", "output": {...}, "durationMs": 1240}

event: agent_completed
data: {"agent": "ResearchAgent", "result": "...", "tokens": 1200, "costUsd": 0.012}

event: orchestrator_result
data: {"result": "...", "totalCostUsd": 0.035, "durationMs": 8400}

event: error
data: {"agent": "CodeAgent", "error": "..."}
```

#### GET /api/v1/agents/sessions
Lists sessions, newest first. Supports pagination.
```
Query params: page (default 1), pageSize (default 20, max 100)

Response 200
{
  "items": [...SessionSummaryDto],
  "totalCount": 142,
  "page": 1,
  "pageSize": 20
}
```

#### GET /api/v1/agents/sessions/{id}
Full session detail including sub-tasks, tool calls, and costs.
```json
// Response 200
{
  "id": "uuid",
  "task": "...",
  "status": "completed",
  "result": "...",
  "totalTokens": 4821,
  "totalCostUsd": "0.014632",
  "createdAt": "...",
  "completedAt": "...",
  "subTasks": [
    {
      "id": "uuid",
      "agentType": "research",
      "taskDescription": "...",
      "status": "completed",
      "result": "...",
      "toolCalls": [...],
      "startedAt": "...",
      "completedAt": "..."
    }
  ],
  "costs": [...]
}
```

#### DELETE /api/v1/agents/sessions/{id}
Deletes a session and all related sub-tasks, tool calls, and messages (CASCADE).
```json
// Response 204 No Content
```

### Admin Endpoints

| Method | Endpoint | Description |
|---|---|---|
| GET | `/admin/usage` | Total sessions, tokens, and cost aggregates |
| GET | `/admin/tool-calls` | Recent tool calls with latency breakdown |
| GET | `/admin/agents` | Per-agent usage and cost breakdown |
| GET | `/admin/health` | API, database, and Anthropic API health check |

#### GET /api/v1/admin/health
```json
{
  "status": "healthy",
  "database": "connected",
  "anthropicApi": "reachable",
  "timestamp": "..."
}
```

---

## 8. CQRS Design

### Commands

#### RunOrchestratorCommand
```csharp
public sealed record RunOrchestratorCommand(
    string Task,
    Guid SessionId,
    int MaxParallelAgents = 3,
    int TimeoutSeconds = 120
) : IRequest<RunOrchestratorResult>;

public sealed record RunOrchestratorResult(
    Guid SessionId,
    string Status,
    string StreamUrl
);
```

Handler responsibilities:
1. Persist `AgentSession` with status `pending`
2. Update status to `running`
3. Call `IOrchestrator.RunAsync(task, sessionId, maxParallelAgents, timeoutSeconds, cancellationToken)`
4. On success: update status to `completed`, persist `result`
5. On failure: update status to `failed`, persist `error_message`
6. Publish SSE completion event via `ISseStreamService`

```csharp
public sealed class RunOrchestratorCommandHandler
    : IRequestHandler<RunOrchestratorCommand, RunOrchestratorResult>
{
    private readonly IOrchestrator _orchestrator;
    private readonly IAgentSessionRepository _sessionRepository;
    private readonly ISseStreamService _sseStreamService;
    private readonly ILogger<RunOrchestratorCommandHandler> _logger;

    public async Task<RunOrchestratorResult> Handle(
        RunOrchestratorCommand request,
        CancellationToken cancellationToken)
    {
        // Create session, fire orchestrator, return session ID and stream URL immediately
    }
}
```

### Queries

#### GetSessionQuery
```csharp
public sealed record GetSessionQuery(Guid SessionId) : IRequest<SessionDetailDto>;
```

Handler: Fetch session + eager-load sub-tasks, tool calls, costs via repository. Map to `SessionDetailDto`. Throw `SessionNotFoundException` if not found.

#### GetAllSessionsQuery
```csharp
public sealed record GetAllSessionsQuery(
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<SessionSummaryDto>>;
```

### Pipeline Behaviors (applied via MediatR registration order)
1. `ValidationBehavior<TRequest, TResponse>` — FluentValidation before handler
2. `LoggingBehavior<TRequest, TResponse>` — structured log on entry/exit with duration

---

## 9. Agent Design

### IAgent Interface
```csharp
public interface IAgent
{
    AgentType Type { get; }
    string Description { get; }
    IReadOnlyList<string> SupportedTools { get; }

    Task<AgentResult> ExecuteAsync(
        SubTask subTask,
        IReadOnlyList<IMcpTool> availableTools,
        CancellationToken cancellationToken = default);
}

public sealed record AgentResult(
    bool Success,
    string? Result,
    string? ErrorMessage,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd
);
```

### IOrchestrator Interface
```csharp
public interface IOrchestrator
{
    Task<OrchestratorResult> RunAsync(
        string task,
        Guid sessionId,
        int maxParallelAgents,
        int timeoutSeconds,
        CancellationToken cancellationToken = default);
}

public sealed record OrchestratorResult(
    bool Success,
    string? Result,
    string? ErrorMessage,
    int TotalInputTokens,
    int TotalOutputTokens,
    decimal TotalCostUsd
);
```

### BaseAgent
Abstract base providing:
- Anthropic client injection via `AnthropicService`
- Tool call dispatch loop (agentic loop) via `McpExecutor`
- Token/cost tracking via `CostCalculator`
- Structured logging (`ILogger<T>`)
- Message persistence via `IAgentMessageRepository`

The agentic loop:
1. Call `AnthropicService.CreateMessageAsync(...)` with tools
2. If `StopReason == "tool_use"`: extract tool call blocks, dispatch each via `McpExecutor`, append results, loop
3. If `StopReason == "end_turn"`: extract text, return `AgentResult`
4. Max iterations guard (default 10) to prevent runaway loops

### OrchestratorAgent
**System prompt focus:** Task decomposition, delegation, aggregation.
```
You are an orchestrator. Given a task, analyze it and create a plan.
Identify which specialized agents are needed:
- ResearchAgent: for web research, information gathering
- CodeAgent: for code generation, file operations
- DataAgent: for data analysis, database queries
Return a JSON plan with subtasks assigned to agents.
Do not perform research, coding, or data analysis yourself. Delegate everything.
```

Execution flow:
1. Call Claude to produce a decomposition plan (JSON)
2. Parse plan into `List<SubTask>`
3. Persist sub-tasks to DB
4. Dispatch each sub-task to the appropriate agent in parallel using `Task.WhenAll` (up to `maxParallelAgents`)
5. Aggregate results into a final summary (second Claude call)
6. Return aggregated `OrchestratorResult`

### ResearchAgent
**System prompt focus:** Web research, source evaluation, concise summarization.
Tools: `WebSearchTool`
```
You are a research specialist. Use web search to gather accurate, up-to-date information
on the given topic. Synthesize findings into a clear, structured summary with sources.
Cite sources. Do not fabricate information.
```

### CodeAgent
**System prompt focus:** Code generation, best practices, file operations.
Tools: `FileSystemTool`
```
You are a code specialist. Generate clean, production-quality code.
Read and write files as needed. Always explain what the code does and why.
Follow language idioms and best practices. Never write placeholder or TODO code.
```

### DataAgent
**System prompt focus:** SQL generation, data analysis, pattern identification.
Tools: `DatabaseTool`
```
You are a data specialist. Query databases, analyze data, identify patterns and insights.
Present findings clearly with supporting data.
Write safe, parameterized SQL. Never run destructive queries (DROP, DELETE, TRUNCATE)
without explicit instruction.
```

---

## 10. MCP Tool Design

### IMcpTool Interface
```csharp
public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    JsonSchema InputSchema { get; }

    Task<McpToolResult> ExecuteAsync(
        JsonElement input,
        CancellationToken cancellationToken = default);
}

public sealed record McpToolResult(
    bool Success,
    JsonElement Output,
    string? ErrorMessage = null,
    int DurationMs = 0
);
```

### IMcpToolRegistry
```csharp
public interface IMcpToolRegistry
{
    IReadOnlyList<IMcpTool> GetToolsForAgent(AgentType agentType);
    IMcpTool? GetTool(string name);
    Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement input,
        CancellationToken cancellationToken = default);
}
```

Registry is registered as a singleton. Tools are auto-discovered at startup via DI — add a new `IMcpTool` implementation and it's available. Tool-to-agent mapping is defined in configuration.

### McpExecutor
Wraps the dispatch loop: accepts a tool name and raw `JsonElement` input, delegates to the registry, logs the call to `tool_calls` table via `ToolCallRepository`, and returns the result. Enforces per-tool timeout (`DEFAULT_TOOL_TIMEOUT_SECONDS`).

### WebSearchTool
```
Name: web_search
Description: Search the web for current information
Input schema: { "query": "string", "maxResults": "integer (default 5)" }
Output: { "results": [{ "title": "...", "url": "...", "snippet": "..." }] }
```
Implementation: Brave Search API or SerpAPI (configurable via `SEARCH_PROVIDER` env var). Respects `maxResults`. Returns structured JSON.

### FileSystemTool
```
Name: file_system
Description: Read and write files in a sandboxed workspace directory
Input schema: {
  "operation": "read | write | list | create_directory",
  "path": "string (relative to workspace root)",
  "content": "string (required for write)"
}
Output: { "content": "..." } | { "files": [...] }
```
Implementation: Sandboxed to a configurable workspace directory (`FILESYSTEM_WORKSPACE`). Rejects path traversal attempts (any `..` in path).

### DatabaseTool
```
Name: database_query
Description: Query the application database
Input schema: {
  "operation": "execute_query | list_tables | describe_table",
  "query": "string (required for execute_query)",
  "table": "string (required for describe_table)",
  "parameters": "object (optional)"
}
Output: { "rows": [...], "rowCount": number, "durationMs": number }
       | { "tables": [...] }
       | { "columns": [...] }
```
Implementation: EF Core raw SQL with a read-only DB connection. `execute_query` enforces SELECT-only (rejects anything matching `INSERT|UPDATE|DELETE|DROP|TRUNCATE|ALTER` patterns after stripping comments). `list_tables` and `describe_table` use information_schema queries.

---

## 11. Frontend Design

### Tech Stack
- React 18 with TypeScript
- Vite (build tool)
- TailwindCSS (styling)
- React Query (server state, session list)
- Native `EventSource` API (SSE consumption)

### Pages
- `/` — Playground (task input + live agent stream)
- `/sessions` — Session history list
- `/sessions/:id` — Session detail with full sub-task breakdown
- `/admin` — Usage and cost dashboard

### Playground UI Behavior
```
1. User types a task in the input box
2. Clicks "Run" → POST /agents/run
3. Session ID returned → SSE connection opens to /agents/sessions/{id}/stream
4. AgentStatusGrid appears with three agent cards: Research | Code | Data
   - Each shows: status (waiting/running/done), tool calls, partial result
5. As SSE events arrive:
   - Agent card updates to "running" with spinner
   - ToolCallLog shows tool calls as they execute (tool name, input, output, duration)
   - Agent card shows "completed" with result when done
6. Final aggregated result streams in via StreamOutput below agent cards
7. Session automatically saved — appears in /sessions history
```

### AgentCard Component
```
┌──────────────────────────────────┐
│ 🔬 Research Agent     ● Running  │
├──────────────────────────────────┤
│ Task: Research .NET 8 perf trends│
│                                  │
│ Tool Calls:                      │
│ ✓ WebSearch(".NET 8 perf") — 1.2s│
│ ✓ WebSearch("System.Threading")  │
│   — 0.9s                         │
│ ⟳ WebSearch("Span<T> perf...")   │
│                                  │
│ Tokens: 1,240 | $0.012           │
└──────────────────────────────────┘
```

```typescript
interface AgentCardProps {
  agentType: 'orchestrator' | 'research' | 'code' | 'data';
  status: 'pending' | 'running' | 'completed' | 'failed';
  streamedContent: string;
  toolCalls: ToolCallEvent[];
  tokens?: number;
  costUsd?: number;
}
```

### useAgentStream Hook
```typescript
const useAgentStream = (sessionId: string | null) => {
  // Creates EventSource for /api/v1/agents/sessions/{sessionId}/stream
  // Parses events by type:
  //   agent_started, tool_call, tool_result, agent_completed,
  //   orchestrator_result, error
  // Returns: agentStates map, isComplete, error, totalCost
};
```

SSE events update per-agent state slices in a `Map<AgentType, AgentState>`. Each `AgentCard` receives its slice. `agent_completed` events fill the card result. `orchestrator_result` fills the `StreamOutput` component.

---

## 12. C# Coding Standards

### Naming
- Classes: `PascalCase`
- Methods: `PascalCase`
- Private fields: `_camelCase`
- Interfaces: `IPrefix`

### Controller Pattern
```csharp
// Controllers are thin — HTTP in, MediatR out
[ApiController]
[Route("api/v1/[controller]")]
public sealed class AgentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AgentsController> _logger;

    [HttpPost("run")]
    [ProducesResponseType(typeof(RunOrchestratorResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunAsync(
        [FromBody] RunOrchestratorRequest request,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.NewGuid();
        var command = new RunOrchestratorCommand(
            request.Task,
            sessionId,
            request.Config?.MaxParallelAgents ?? 3,
            request.Config?.TimeoutSeconds ?? 120);
        var result = await _mediator.Send(command, cancellationToken);
        return Accepted(new RunOrchestratorResponse(
            result.SessionId,
            result.Status,
            $"/api/v1/agents/sessions/{result.SessionId}/stream"));
    }
}
```

### Repository Pattern
```csharp
public interface IAgentSessionRepository
{
    Task<AgentSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<AgentSession>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(AgentSession session, CancellationToken cancellationToken = default);
    Task UpdateAsync(AgentSession session, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
```

### Exception Hierarchy
```csharp
public abstract class OrchestAIException : Exception
{
    public string ErrorCode { get; }
    protected OrchestAIException(string errorCode, string message, Exception? inner = null)
        : base(message, inner) => ErrorCode = errorCode;
}

public sealed class AgentExecutionException : OrchestAIException
{
    public AgentType AgentType { get; }
    public AgentExecutionException(AgentType agentType, string message, Exception? inner = null)
        : base("AGENT_EXECUTION_FAILED", message, inner) => AgentType = agentType;
}

public sealed class SessionNotFoundException : OrchestAIException
{
    public SessionNotFoundException(Guid sessionId)
        : base("SESSION_NOT_FOUND", $"Session {sessionId} not found.") { }
}

public sealed class McpToolException : OrchestAIException
{
    public string ToolName { get; }
    public McpToolException(string toolName, string message, Exception? inner = null)
        : base("MCP_TOOL_ERROR", message, inner) => ToolName = toolName;
}
```

### Async Rules
- Every method that touches I/O is `async Task<T>`
- Every I/O method accepts and forwards `CancellationToken`
- Never `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` — no exceptions
- `ConfigureAwait(false)` in library projects (Domain, Infrastructure, Application); not needed in API project

### Structured Logging
```csharp
_logger.LogInformation(
    "Agent {AgentType} started sub-task {SubTaskId} for session {SessionId}",
    agent.Type, subTask.Id, session.Id);

_logger.LogError(
    "Agent {AgentType} failed: {Error}",
    agent.Type, ex.Message);
```

### React / Frontend Standards
- All API calls via `services/` layer — no direct `fetch` in components
- SSE handled exclusively by `useAgentStream` hook — no `EventSource` outside it
- No inline styles — TailwindCSS only
- Components are pure — no side effects, no direct API calls
- Server state via React Query; local UI state via `useState`
- TypeScript strict mode — no `any` types
- Named exports only — no default exports

---

## 13. Non-Functional Requirements

| Requirement | Target |
|---|---|
| Task completion time | < 120 seconds for typical 3-agent task |
| Parallel agent execution | All agents run simultaneously, not sequential |
| SSE event latency | < 500ms from event to UI update |
| Tool call timeout | 30 seconds per tool call |
| Failed agent handling | Partial failure returns partial result, not full failure |
| Cost per session | Tracked per agent and total — target < $0.10 average |
| API availability | Health check at `/admin/health` — required for Railway deploys |

---

## 14. Environment Variables

```bash
# API
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:5000

# Database
DATABASE_URL=Host=localhost;Port=5432;Database=orchestai;Username=postgres;Password=postgres

# Anthropic
ANTHROPIC_API_KEY=sk-ant-...
ANTHROPIC_MODEL=claude-sonnet-4-6

# MCP Tools — Web Search
SEARCH_API_KEY=...               # Brave Search or SerpAPI key
SEARCH_PROVIDER=brave            # brave | serpapi

# MCP Tools — File System
FILESYSTEM_WORKSPACE=/tmp/orchestai   # sandboxed file operations root

# Agent Limits
MAX_PARALLEL_AGENTS=3
DEFAULT_TIMEOUT_SECONDS=120

# Frontend (Vite)
VITE_API_BASE_URL=http://localhost:5000
```

---

## 15. Docker Compose Local Setup

```yaml
# docker-compose.yml
services:
  postgres:
    image: postgres:15-alpine
    environment:
      POSTGRES_DB: orchestai
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 5s
      timeout: 5s
      retries: 5

  api:
    build:
      context: ./backend
      dockerfile: Dockerfile
    ports:
      - "5000:5000"
    environment:
      DATABASE_URL: Host=postgres;Port=5432;Database=orchestai;Username=postgres;Password=postgres
      ANTHROPIC_API_KEY: ${ANTHROPIC_API_KEY}
      ANTHROPIC_MODEL: claude-sonnet-4-6
      SEARCH_API_KEY: ${SEARCH_API_KEY}
      SEARCH_PROVIDER: ${SEARCH_PROVIDER:-brave}
      FILESYSTEM_WORKSPACE: /tmp/orchestai
      MAX_PARALLEL_AGENTS: 3
      DEFAULT_TIMEOUT_SECONDS: 120
    depends_on:
      postgres:
        condition: service_healthy
    volumes:
      - workspace:/tmp/orchestai

  web:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    ports:
      - "3000:3000"
    environment:
      VITE_API_BASE_URL: http://localhost:5000
    depends_on:
      - api

volumes:
  postgres_data:
  workspace:
```

Local dev startup:
```bash
git clone https://github.com/jigargajjarcad/orchestai
cd orchestai
cp docker-compose.override.yml.example docker-compose.override.yml
# Add ANTHROPIC_API_KEY and SEARCH_API_KEY to docker-compose.override.yml
docker compose up

# API:      http://localhost:5000
# Swagger:  http://localhost:5000/swagger
# Frontend: http://localhost:3000
# DB:       localhost:5432
```

---

## 16. Deployment

### Backend + Database → Railway
1. Create Railway project
2. Add PostgreSQL plugin — Railway provides `DATABASE_URL`
3. Connect GitHub repo, set root to `/backend`
4. Set environment variables: `ANTHROPIC_API_KEY`, `SEARCH_API_KEY`, `SEARCH_PROVIDER`, `FILESYSTEM_WORKSPACE=/tmp/orchestai`, `MAX_PARALLEL_AGENTS`, `DEFAULT_TIMEOUT_SECONDS`
5. Railway auto-detects .NET and builds with `dotnet publish`
6. Migrations run on startup via `dbContext.Database.MigrateAsync()` in `Program.cs`

### Frontend → Vercel
1. Import GitHub repo on Vercel
2. Set root directory to `/frontend`
3. Set `VITE_API_BASE_URL` to Railway API URL
4. Vercel auto-detects Vite and deploys

### CORS
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(builder.Configuration["FRONTEND_URL"] ?? "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});
```

### CI/CD — GitHub Actions
```yaml
# .github/workflows/ci.yml
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Build backend
        run: dotnet build backend/OrchestAI.sln --configuration Release

      - name: Test backend
        run: dotnet test backend/OrchestAI.sln --configuration Release --no-build

      - name: Setup Node
        uses: actions/setup-node@v4
        with:
          node-version: '20'
          cache: 'npm'
          cache-dependency-path: frontend/package-lock.json

      - name: Install frontend deps
        run: npm ci --prefix frontend

      - name: Build frontend
        run: npm run build --prefix frontend
```

---

## 17. Architecture Principles

These apply to every future build in this repo — Claude Code follows them without being re-asked:

- **Clean architecture** — dependencies flow Domain → Application → Infrastructure → API; nothing in Domain references outer layers
- **CQRS throughout** — controllers handle HTTP only; all logic lives in MediatR command/query handlers
- **Always async/await with `CancellationToken`** — no `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`, no exceptions
- **Production quality always** — proper error handling, structured logging, and cost tracking on every agent operation; no "simplified for demo" shortcuts
- **No manual coding** — Claude Code writes everything; the human directs, reviews, and architects
- **Every feature needs tests before it's done** — unit tests for handlers/agents/tools, integration tests for end-to-end flows
- **Every architectural decision goes in `DECISIONS.md`** — non-obvious tradeoffs get an ADR with context and a trigger for revisiting

---

## 18. Priority Framework

Every roadmap item below is tagged with a priority:

| Tag | Meaning |
|---|---|
| **P0** | Enterprise blocker — without this, enterprises can't adopt |
| **P1** | Differentiator — makes OrchestAI better than competitors |
| **P2** | Growth — drives community adoption and GitHub stars |
| **P3** | Revenue — enables paid tiers |

---

## 19. Product Strategy

### Product Sequence: Framework → Observability Platform → Cloud
Three stages, in this order, each gated on the previous one proving itself:

1. **Framework** (Weeks 1–6, shipped) — the open-source CQRS multi-agent framework
   itself. This is the adoption driver: free, self-hosted, gives enterprise .NET
   teams a reason to install OrchestAI and put it in front of their own workloads.
2. **Observability Platform** (Weeks 7–8) — this is the monetization path.
   Teams running multi-agent systems in production need execution visibility, cost
   tracking, and quality signal that a self-hosted open-source framework doesn't
   provide out of the box. Observability is the wedge from "free framework" to
   "paid product."
3. **Cloud** (later, timeline TBD) — a hosted version of the observability
   platform, gated on Weeks 7–8 actually proving customer demand. Cloud is not
   scheduled until Observability has real usage signal to build on — building
   hosted infrastructure before anyone has asked for it is the wrong order.

### Use Case Framing Principle
Every feature is named and scoped as the enterprise problem it solves, not the
API it wraps. This governs both marketing copy and how features get scoped —
"a use case" implies an opinionated agent + workflow; "a connector" implies a
thin tool wrapper, and the latter is not the product.

| Say this | Not this |
|---|---|
| AI Code Review Pipeline | GitHub Tool |
| Sprint Manager Agent | Jira Tool |
| Business Intelligence Agent | SQL Server Tool |

### Build in Public
One LinkedIn post per week on an architectural decision from that week's build
(a real trade-off, an ADR, a bug found in production — not a feature announcement).
Goal: become known as the person who built the .NET AI orchestration framework,
compounding portfolio credibility with each post rather than a one-time launch.

---

## 20. Roadmap

> **Note on "Phase" numbering:** the "Phase 1"/"Phase 2" labels immediately below are this
> document's own original roadmap-stage numbering, written before Week 7 shipped, and are
> unrelated to the "Phase 1 / Phase 2 / Phase 3" progression actually delivered and tracked in
> `DECISIONS.md` (ADR-017 onward), `docs/phase3-domain-notes.md`, and `docs/superpowers/plans/`.
> The real delivered phases are: **Phase 1** = architecture/product validation (live end-to-end
> verification of Weeks 7–12), **Phase 2** = NuGet packaging validation, **Phase 3** =
> Sports/Athlete Performance domain investigation and sample application (current, in progress).
> The Week 7–12 content below shipped as described; this section's own historical "Phase
> 1"/"Phase 2" headers are left as originally written, not renamed.

### Completed — Weeks 1–6
Foundation → Agent Core → MCP Integration → Frontend (Weeks 1–4) → multi-provider
LLM support, human-in-the-loop approval gates, Manager Agent review pass (Week 5)
→ per-agent checkpointing, user memory, SQL Server tool, retry with backoff, PII
redaction (Week 6). See [Current State](#2-current-state-week-4-complete) and
`DECISIONS.md` for what shipped and why.

### Phase 1 — Weeks 7–12: Observability, Evaluation, Distribution, Enterprise Use Cases

**Week 7 — Observability Foundation** — **P1**
- Execution timeline view (per-task, per-agent, per-tool-call)
- Cost dashboard (spend by agent, by model, by task, over time)
- Error rate tracking
- Prompt performance comparison (same task, different prompts/models)

**Week 8 — AI Evaluation** — **P1**
- Per-execution scoring
- Hallucination risk signal
- Task completion rate
- Output quality scoring
- New `EvaluationResults` table

**Week 9 — NuGet Package + AI Code Review Pipeline** — **P2**
- OrchestAI published as a NuGet package (`dotnet add package OrchestAI`)
- AI Code Review Pipeline use case (built on GitHub's API — framed and shipped as
  the use case, not "a GitHub tool")

**Week 10 — Azure Ecosystem** — **P2**
- Azure Blob Storage
- Azure DevOps
- SQL Server patterns (building on the Week 6 `DatabaseTool` foundation)

**Week 11–12 — Enterprise Use Cases** — **P1**
- Sprint Manager Agent
- Business Intelligence Agent
- Executive Assistant Agent
- Team Notification Agent

### Phase 2 — Cloud (timeline TBD)
Hosted version of the Week 7–8 observability platform. Not scheduled until
Observability has proven real customer demand — see Product Sequence above.
- OrchestAI Cloud beta — **P3**
- Google / AWS / Ollama providers — **P3**
- Visual workflow builder — **P3**

### Postponed Indefinitely
SharePoint, Power BI, SAP, Salesforce, PagerDuty, Datadog. Revisit only on
explicit enterprise customer request — none of these serve the Week 7–12
sequence and speculative integration work here is exactly the kind of
connector-first thinking the Use Case Framing Principle exists to avoid.
