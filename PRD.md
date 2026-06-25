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
- .NET engineers building AI-powered products
- AI engineers needing a production-grade agent framework that isn't Python
- Engineering teams already on ASP.NET Core who want multi-agent capabilities without adopting a second language
- Portfolio showcase for AI Engineer roles in the .NET space

### Key Differentiator
First open-source, production-grade, multi-agent CQRS framework for .NET 8. Not a toy. Not a proof of concept. Deployable, observable, and extensible.

---

## 2. System Architecture

### High-Level Flow
```
User Task (HTTP POST)
  → ASP.NET Core API
  → MediatR → RunOrchestratorCommand
  → OrchestratorAgent (Claude claude-sonnet-4-6)
    → Decompose task into SubTasks
    → Dispatch in parallel:
        ResearchAgent  → WebSearchTool
        CodeAgent      → FileSystemTool
        DataAgent      → DatabaseTool
    → Aggregate sub-agent results
  → Stream via SSE
  → React UI (AgentCard per agent, live token stream)
```

### Components
| Layer | Technology | Responsibility |
|---|---|---|
| API | ASP.NET Core 8 | HTTP endpoints, SSE streaming, request validation |
| Application | MediatR + CQRS | Commands, queries, handlers, DTOs, pipeline behaviors |
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

## 3. Database Schema

All tables use `uuid` primary keys, `timestamptz` timestamps, and soft-delete via `deleted_at` where appropriate.

### agent_sessions
Represents one user-initiated orchestration run.
```sql
CREATE TABLE agent_sessions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_task       TEXT NOT NULL,
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
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id      UUID NOT NULL REFERENCES agent_sessions(id) ON DELETE CASCADE,
    agent_type      VARCHAR(50) NOT NULL,
    -- orchestrator | research | code | data
    task_description TEXT NOT NULL,
    status          VARCHAR(50) NOT NULL DEFAULT 'pending',
    -- pending | running | completed | failed
    result          TEXT,
    error_message   TEXT,
    started_at      TIMESTAMPTZ,
    completed_at    TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_sub_tasks_session_id ON sub_tasks(session_id);
```

### tool_calls
Audit log for every MCP tool invocation.
```sql
CREATE TABLE tool_calls (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    sub_task_id     UUID NOT NULL REFERENCES sub_tasks(id) ON DELETE CASCADE,
    tool_name       VARCHAR(100) NOT NULL,
    input_json      JSONB NOT NULL,
    output_json     JSONB,
    status          VARCHAR(50) NOT NULL DEFAULT 'pending',
    -- pending | success | error
    error_message   TEXT,
    duration_ms     INTEGER,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_tool_calls_sub_task_id ON tool_calls(sub_task_id);
```

### agent_messages
Full message history for every agent conversation (system + user + assistant turns).
```sql
CREATE TABLE agent_messages (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    sub_task_id     UUID NOT NULL REFERENCES sub_tasks(id) ON DELETE CASCADE,
    role            VARCHAR(20) NOT NULL,
    -- system | user | assistant | tool_result
    content         TEXT NOT NULL,
    token_count     INTEGER,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_agent_messages_sub_task_id ON agent_messages(sub_task_id);
```

### session_costs
Token and cost breakdown per sub-task for observability.
```sql
CREATE TABLE session_costs (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    session_id          UUID NOT NULL REFERENCES agent_sessions(id) ON DELETE CASCADE,
    sub_task_id         UUID REFERENCES sub_tasks(id) ON DELETE SET NULL,
    input_tokens        INTEGER NOT NULL DEFAULT 0,
    output_tokens       INTEGER NOT NULL DEFAULT 0,
    cache_read_tokens   INTEGER NOT NULL DEFAULT 0,
    cache_write_tokens  INTEGER NOT NULL DEFAULT 0,
    cost_usd            DECIMAL(10, 6) NOT NULL DEFAULT 0,
    model               VARCHAR(100) NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_session_costs_session_id ON session_costs(session_id);
```

### mcp_tools
Registry of available MCP tools and their metadata.
```sql
CREATE TABLE mcp_tools (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name            VARCHAR(100) NOT NULL UNIQUE,
    description     TEXT NOT NULL,
    input_schema    JSONB NOT NULL,
    -- JSON Schema for tool input validation
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
```

---

## 4. Project Structure

```
orchestai/
├── PRD.md
├── CLAUDE.md
├── README.md
├── .gitignore
├── docker-compose.yml
├── docker-compose.override.yml        # local dev secrets
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
│   │   │   │   └── ListSessions/
│   │   │   │       ├── ListSessionsQuery.cs
│   │   │   │       └── ListSessionsQueryHandler.cs
│   │   │   ├── DTOs/
│   │   │   │   ├── AgentSessionDto.cs
│   │   │   │   ├── SubTaskDto.cs
│   │   │   │   ├── ToolCallDto.cs
│   │   │   │   └── SessionCostDto.cs
│   │   │   ├── Behaviors/
│   │   │   │   ├── LoggingBehavior.cs
│   │   │   │   └── ValidationBehavior.cs
│   │   │   └── Common/
│   │   │       └── IStreamingNotificationHandler.cs
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
│   │   │   │   ├── IMcpTool.cs
│   │   │   │   ├── IAgentSessionRepository.cs
│   │   │   │   ├── ISubTaskRepository.cs
│   │   │   │   └── IMcpToolRegistry.cs
│   │   │   └── Exceptions/
│   │   │       ├── OrchestAIException.cs
│   │   │       ├── AgentExecutionException.cs
│   │   │       ├── SessionNotFoundException.cs
│   │   │       └── McpToolException.cs
│   │   │
│   │   └── OrchestAI.Infrastructure/
│   │       ├── OrchestAI.Infrastructure.csproj
│   │       ├── Agents/
│   │       │   ├── BaseAgent.cs
│   │       │   ├── OrchestratorAgent.cs
│   │       │   ├── ResearchAgent.cs
│   │       │   ├── CodeAgent.cs
│   │       │   └── DataAgent.cs
│   │       ├── Mcp/
│   │       │   ├── McpToolRegistry.cs
│   │       │   ├── Tools/
│   │       │   │   ├── WebSearchTool.cs
│   │       │   │   ├── FileSystemTool.cs
│   │       │   │   └── DatabaseTool.cs
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
│   │       │   │   └── SubTaskRepository.cs
│   │       │   └── Migrations/
│   │       └── Extensions/
│   │           └── InfrastructureServiceExtensions.cs
│   │
│   └── tests/
│       ├── OrchestAI.Application.Tests/
│       │   ├── OrchestAI.Application.Tests.csproj
│       │   ├── Commands/
│       │   │   └── RunOrchestratorCommandHandlerTests.cs
│       │   └── Queries/
│       │       └── GetSessionQueryHandlerTests.cs
│       └── OrchestAI.Infrastructure.Tests/
│           ├── OrchestAI.Infrastructure.Tests.csproj
│           └── Agents/
│               └── OrchestratorAgentTests.cs
│
└── frontend/
    ├── package.json
    ├── tsconfig.json
    ├── tailwind.config.js
    ├── vite.config.ts
    ├── index.html
    └── src/
        ├── main.tsx
        ├── App.tsx
        ├── api/
        │   ├── agentsApi.ts
        │   └── sessionsApi.ts
        ├── components/
        │   ├── Playground/
        │   │   ├── Playground.tsx
        │   │   └── TaskInput.tsx
        │   ├── AgentCard/
        │   │   ├── AgentCard.tsx
        │   │   └── AgentStatusBadge.tsx
        │   ├── Stream/
        │   │   ├── StreamConsumer.tsx
        │   │   └── StreamEvent.tsx
        │   └── Sessions/
        │       ├── SessionList.tsx
        │       └── SessionDetail.tsx
        ├── hooks/
        │   ├── useAgentStream.ts
        │   └── useSessions.ts
        └── types/
            ├── session.ts
            └── stream.ts
```

---

## 5. API Design

### Base URL
`/api/v1`

### Endpoints

#### POST /api/v1/agents/run
Starts an orchestration run. Returns a `sessionId` for SSE subscription.
```json
// Request
{
  "task": "Research the latest .NET 8 performance improvements and write a summary with code examples"
}

// Response 202 Accepted
{
  "sessionId": "uuid",
  "streamUrl": "/api/v1/agents/stream/uuid"
}
```

#### GET /api/v1/agents/stream/{sessionId}
SSE endpoint. `Content-Type: text/event-stream`. Streams events until session completes.
```
event: agent_started
data: {"agentType":"orchestrator","subTaskId":"uuid","timestamp":"..."}

event: token
data: {"agentType":"research","subTaskId":"uuid","token":"Breaking "}

event: tool_called
data: {"agentType":"research","tool":"web_search","input":{"query":"..."}}

event: tool_result
data: {"agentType":"research","tool":"web_search","success":true,"durationMs":342}

event: agent_completed
data: {"agentType":"research","subTaskId":"uuid","result":"...","tokens":1240}

event: session_completed
data: {"sessionId":"uuid","totalTokens":4821,"totalCostUsd":"0.014632"}

event: session_failed
data: {"sessionId":"uuid","error":"Agent execution failed: ..."}
```

#### GET /api/v1/sessions
Lists sessions, newest first. Supports pagination.
```
Query params: page (default 1), pageSize (default 20, max 100)

Response 200
{
  "items": [...AgentSessionDto],
  "totalCount": 142,
  "page": 1,
  "pageSize": 20
}
```

#### GET /api/v1/sessions/{sessionId}
Full session detail including sub-tasks, tool calls, and costs.
```json
// Response 200
{
  "id": "uuid",
  "userTask": "...",
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

#### GET /api/v1/admin/tools
Lists registered MCP tools and their status.
```json
// Response 200
[
  {
    "name": "web_search",
    "description": "...",
    "isActive": true,
    "inputSchema": {...}
  }
]
```

#### GET /api/v1/admin/health
Health check endpoint.
```json
{
  "status": "healthy",
  "database": "connected",
  "anthropicApi": "reachable",
  "timestamp": "..."
}
```

---

## 6. CQRS Design

### Commands

#### RunOrchestratorCommand
```csharp
public sealed record RunOrchestratorCommand(
    string Task,
    string SessionId
) : IRequest<RunOrchestratorResult>;

public sealed record RunOrchestratorResult(
    string SessionId,
    string Status
);
```

Handler responsibilities:
1. Persist `AgentSession` with status `pending`
2. Update status to `running`
3. Call `OrchestratorAgent.RunAsync(task, sessionId, cancellationToken)`
4. On success: update status to `completed`, persist `result`
5. On failure: update status to `failed`, persist `error_message`
6. Publish SSE completion event

### Queries

#### GetSessionQuery
```csharp
public sealed record GetSessionQuery(string SessionId) : IRequest<AgentSessionDto?>;
```

Handler: Fetch session + eager-load sub-tasks, tool calls, costs via repository. Map to DTO. Throw `SessionNotFoundException` if not found.

#### ListSessionsQuery
```csharp
public sealed record ListSessionsQuery(
    int Page = 1,
    int PageSize = 20
) : IRequest<PagedResult<AgentSessionDto>>;
```

### Pipeline Behaviors (applied via MediatR registration order)
1. `ValidationBehavior<TRequest, TResponse>` — FluentValidation before handler
2. `LoggingBehavior<TRequest, TResponse>` — structured log on entry/exit with duration

---

## 7. Agent Design

### IAgent Interface
```csharp
public interface IAgent
{
    AgentType AgentType { get; }

    Task<AgentResult> RunAsync(
        AgentContext context,
        CancellationToken cancellationToken = default);
}

public sealed record AgentContext(
    string SessionId,
    string SubTaskId,
    string Task,
    IReadOnlyList<string> AvailableTools,
    Func<StreamEvent, Task> OnStreamEvent
);

public sealed record AgentResult(
    bool Success,
    string? Result,
    string? ErrorMessage,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd
);
```

### BaseAgent
Abstract base providing:
- Anthropic client injection
- Tool call dispatch loop (agentic loop)
- Token/cost tracking
- Structured logging (`ILogger<T>`)
- Message persistence via `IAgentMessageRepository`

The agentic loop:
1. Call `client.Messages.CreateAsync(...)` with tools
2. If `StopReason == "tool_use"`: extract tool call blocks, dispatch each via `IMcpToolRegistry`, append results, loop
3. If `StopReason == "end_turn"`: extract text, return `AgentResult`
4. Max iterations guard (default 10) to prevent runaway loops

### OrchestratorAgent
**System prompt focus:** Task decomposition, delegation, aggregation.
```
You are an orchestration agent. Your job is to:
1. Analyze the user's task
2. Decompose it into specialist sub-tasks
3. Return a JSON plan: { "subTasks": [{ "agentType": "research|code|data", "task": "..." }] }
Do not perform research, coding, or data analysis yourself. Delegate everything.
```

Execution flow:
1. Call Claude to produce a decomposition plan (JSON)
2. Parse plan into `List<SubTask>`
3. Persist sub-tasks to DB
4. Dispatch each sub-task to the appropriate agent in parallel using `Task.WhenAll`
5. Aggregate results into a final summary (second Claude call)
6. Return aggregated result

### ResearchAgent
**System prompt focus:** Web research, source evaluation, concise summarization.
Tools: `WebSearchTool`
```
You are a research specialist. Use web_search to gather accurate, up-to-date information.
Cite sources. Summarize findings concisely. Do not fabricate information.
```

### CodeAgent
**System prompt focus:** Code generation, best practices, file operations.
Tools: `FileSystemTool`
```
You are a code generation specialist. Write production-quality code.
Follow language idioms and best practices. Use file operations to read context and write outputs.
Always include error handling. Never write placeholder or TODO code.
```

### DataAgent
**System prompt focus:** SQL generation, data analysis, pattern identification.
Tools: `DatabaseTool`
```
You are a data analysis specialist. Query databases to extract insights.
Write safe, parameterized SQL. Summarize findings with key metrics and patterns.
Never run destructive queries (DROP, DELETE, TRUNCATE) without explicit instruction.
```

---

## 8. MCP Tool Design

### IMcpTool Interface
```csharp
public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    JsonElement InputSchema { get; }

    Task<McpToolResult> ExecuteAsync(
        JsonElement input,
        CancellationToken cancellationToken = default);
}

public sealed record McpToolResult(
    bool Success,
    JsonElement? Output,
    string? ErrorMessage,
    int DurationMs
);
```

### McpToolRegistry
```csharp
public interface IMcpToolRegistry
{
    IReadOnlyList<IMcpTool> GetToolsForAgent(AgentType agentType);
    Task<McpToolResult> ExecuteToolAsync(
        string toolName,
        JsonElement input,
        CancellationToken cancellationToken = default);
}
```

Registry is registered as a singleton. Tools are registered at startup via DI and keyed by name. Tool-to-agent mapping is defined in configuration.

### WebSearchTool
```
Name: web_search
Description: Search the web for current information
Input schema: { "query": "string", "maxResults": "integer (default 5)" }
Output: { "results": [{ "title": "...", "url": "...", "snippet": "..." }] }
```
Implementation: Tavily Search API (or SerpAPI) HTTP client. Respects `maxResults`. Returns structured JSON.

### FileSystemTool
```
Name: file_system
Description: Read and write files in a sandboxed workspace directory
Input schema: {
  "operation": "read | write | list",
  "path": "string (relative to workspace root)",
  "content": "string (required for write)"
}
Output: { "content": "..." } | { "files": [...] }
```
Implementation: Sandboxed to a configurable workspace directory (`WORKSPACE_PATH`). Rejects path traversal attempts (any `..` in path).

### DatabaseTool
```
Name: database_query
Description: Execute a read-only SQL query against the application database
Input schema: { "query": "string", "parameters": "object (optional)" }
Output: { "rows": [...], "rowCount": number, "durationMs": number }
```
Implementation: EF Core raw SQL with a read-only DB connection. Enforces SELECT-only (rejects anything matching `INSERT|UPDATE|DELETE|DROP|TRUNCATE|ALTER` patterns after stripping comments).

---

## 9. Frontend Design

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

### Key Components

#### Playground
```
┌─────────────────────────────────────────────────────────┐
│  OrchestAI Playground                                   │
│                                                         │
│  Task: [Research .NET 8 perf and write examples____]    │
│                                                         │
│  [ Run Agents ]                                         │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │ OrchestratorAgent  [running]                     │  │
│  │ Decomposing task into sub-tasks...               │  │
│  └──────────────────────────────────────────────────┘  │
│  ┌─────────────────────┐  ┌────────────────────────┐   │
│  │ ResearchAgent [run] │  │ CodeAgent      [pend]  │   │
│  │ Searching: .NET 8   │  │ Waiting for research   │   │
│  │ perf improvements.. │  │ results...             │   │
│  └─────────────────────┘  └────────────────────────┘   │
│                                                         │
│  Cost: $0.0142  |  Tokens: 4,821                       │
└─────────────────────────────────────────────────────────┘
```

#### AgentCard
Props:
```typescript
interface AgentCardProps {
  agentType: 'orchestrator' | 'research' | 'code' | 'data';
  status: 'pending' | 'running' | 'completed' | 'failed';
  streamedContent: string;
  toolCalls: ToolCallEvent[];
  tokens?: number;
}
```

#### useAgentStream Hook
```typescript
const useAgentStream = (sessionId: string | null) => {
  // Creates EventSource for /api/v1/agents/stream/{sessionId}
  // Parses events by type: agent_started, token, tool_called, tool_result, agent_completed, session_completed, session_failed
  // Returns: agentStates map, isComplete, error, totalCost
};
```

SSE events update per-agent state slices in a `Map<AgentType, AgentState>`. Each `AgentCard` receives its slice. Token events are appended to the agent's `streamedContent` string for live display.

---

## 10. C# Coding Standards

### Controller Pattern
```csharp
// Controllers are thin — HTTP in, MediatR out
[ApiController]
[Route("api/v1/[controller]")]
public sealed class AgentsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AgentsController> _logger;

    // Constructor injection only

    [HttpPost("run")]
    [ProducesResponseType(typeof(RunOrchestratorResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RunAsync(
        [FromBody] RunOrchestratorRequest request,
        CancellationToken cancellationToken)
    {
        var command = new RunOrchestratorCommand(request.Task, Guid.NewGuid().ToString());
        var result = await _mediator.Send(command, cancellationToken);
        return Accepted(new RunOrchestratorResponse(result.SessionId, $"/api/v1/agents/stream/{result.SessionId}"));
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
// Use structured log properties, not string interpolation
_logger.LogInformation(
    "Agent {AgentType} started sub-task {SubTaskId} for session {SessionId}",
    agentType, subTaskId, sessionId);

// Time operations with LoggerMessage or ActivitySource
using var activity = ActivitySource.StartActivity("agent.run");
activity?.SetTag("agent.type", agentType.ToString());
```

---

## 11. Environment Variables

```bash
# API
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:8080

# Database
DATABASE_URL=Host=localhost;Port=5432;Database=orchestai;Username=postgres;Password=postgres

# Anthropic
ANTHROPIC_API_KEY=sk-ant-...
ANTHROPIC_MODEL=claude-sonnet-4-6

# MCP Tools
TAVILY_API_KEY=tvly-...        # WebSearchTool
WORKSPACE_PATH=/tmp/workspace  # FileSystemTool sandbox root

# Frontend (Vite)
VITE_API_BASE_URL=http://localhost:8080
```

---

## 12. Docker Compose Local Setup

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
      - "8080:8080"
    environment:
      DATABASE_URL: Host=postgres;Port=5432;Database=orchestai;Username=postgres;Password=postgres
      ANTHROPIC_API_KEY: ${ANTHROPIC_API_KEY}
      ANTHROPIC_MODEL: claude-sonnet-4-6
      TAVILY_API_KEY: ${TAVILY_API_KEY}
      WORKSPACE_PATH: /tmp/workspace
    depends_on:
      postgres:
        condition: service_healthy
    volumes:
      - workspace:/tmp/workspace

  frontend:
    build:
      context: ./frontend
      dockerfile: Dockerfile
    ports:
      - "3000:3000"
    environment:
      VITE_API_BASE_URL: http://localhost:8080
    depends_on:
      - api

volumes:
  postgres_data:
  workspace:
```

Local dev startup:
```bash
cp docker-compose.override.yml.example docker-compose.override.yml
# Add your ANTHROPIC_API_KEY and TAVILY_API_KEY
docker compose up -d
# API: http://localhost:8080
# Frontend: http://localhost:3000
# DB: localhost:5432
```

---

## 13. Deployment

### Railway (API + Database)
1. Create Railway project
2. Add PostgreSQL plugin — Railway provides `DATABASE_URL`
3. Connect GitHub repo, set root to `/backend`
4. Set environment variables: `ANTHROPIC_API_KEY`, `TAVILY_API_KEY`, `WORKSPACE_PATH=/tmp/workspace`
5. Railway auto-detects .NET and builds with `dotnet publish`
6. Migrations run on startup via `dbContext.Database.MigrateAsync()` in `Program.cs`

### Vercel (Frontend)
1. Import GitHub repo on Vercel
2. Set root directory to `/frontend`
3. Set `VITE_API_BASE_URL` to Railway API URL
4. Vercel auto-detects Vite and deploys

### CORS
API must allow Vercel frontend origin in production. Configure in `Program.cs`:
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(builder.Configuration["FRONTEND_URL"] ?? "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod());
});
```

---

## 14. Build Order — 4 Weeks

### Week 1 — Foundation
- [ ] Docker Compose: PostgreSQL + API service skeleton
- [ ] .NET solution: 4 projects with correct project references
- [ ] Domain layer: all entities, enums, interfaces, exceptions
- [ ] Infrastructure: EF Core `OrchestAIDbContext` with all configurations
- [ ] Initial migration: create all 6 tables
- [ ] Repository implementations (AgentSessionRepository, SubTaskRepository)
- [ ] MediatR setup: `RunOrchestratorCommand` handler stub, `GetSessionQuery` handler
- [ ] API: `AgentsController` (POST /run returns 202, GET /stream placeholder), `SessionsController`
- [ ] Pipeline behaviors: `LoggingBehavior`, `ValidationBehavior`
- [ ] Health check endpoint

**Done when:** `POST /api/v1/agents/run` persists a session and returns `202`, `GET /api/v1/sessions/{id}` returns the session.

### Week 2 — Agent Core
- [ ] Anthropic SDK integration (`Anthropic.SDK` NuGet)
- [ ] `BaseAgent` with agentic loop (tool call dispatch, max iterations guard)
- [ ] `OrchestratorAgent`: task decomposition + aggregation
- [ ] `ResearchAgent`: system prompt + tool binding
- [ ] `CodeAgent`: system prompt + tool binding
- [ ] `DataAgent`: system prompt + tool binding
- [ ] `McpToolRegistry` with DI registration
- [ ] `WebSearchTool` (Tavily API integration)
- [ ] `FileSystemTool` (sandboxed read/write/list)
- [ ] `DatabaseTool` (read-only SQL, SELECT enforcement)
- [ ] Token/cost tracking → `session_costs` table

**Done when:** A real multi-agent run completes end-to-end in logs (no frontend yet).

### Week 3 — Streaming + Frontend
- [ ] SSE endpoint (`GET /api/v1/agents/stream/{sessionId}`)
- [ ] `IStreamPublisher` abstraction + in-memory channel implementation
- [ ] `RunOrchestratorCommandHandler` publishes all SSE events
- [ ] React app scaffold (Vite + TypeScript + Tailwind)
- [ ] `useAgentStream` hook (EventSource consumer)
- [ ] `AgentCard` component
- [ ] `Playground` page (task input → run → live cards)
- [ ] `SessionList` + `SessionDetail` pages
- [ ] Error display (session_failed event)
- [ ] Cost/token display in UI

**Done when:** Full end-to-end: browser submits task, watches agents stream live, sees results.

### Week 4 — Polish + Ship
- [ ] Admin endpoints: tool list, health check
- [ ] `ExceptionHandlingMiddleware` (structured error responses)
- [ ] `RequestLoggingMiddleware`
- [ ] Dockerfile for API (multi-stage build)
- [ ] Dockerfile for frontend
- [ ] `docker-compose.yml` complete
- [ ] Unit tests: command handler, query handler, OrchestratorAgent decomposition
- [ ] README: setup guide, architecture diagram, API reference, deployment guide
- [ ] Railway deployment
- [ ] Vercel deployment
- [ ] `.env.example` + `docker-compose.override.yml.example`

**Done when:** Live public URL, README complete, repo public on GitHub.
