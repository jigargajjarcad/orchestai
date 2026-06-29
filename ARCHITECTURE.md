# OrchestAI — Architecture Reference

## 1. System Overview

OrchestAI is a production-grade multi-agent AI orchestration framework for .NET 8. It bridges the gap between enterprise .NET architecture (CQRS, MediatR, clean architecture) and modern agentic AI patterns (tool use, agent chaining, real-time streaming).

The system is divided into four layers following clean architecture principles:

```
OrchestAI.API           → HTTP boundary (controllers, SSE, middleware)
OrchestAI.Application   → Use-case layer (CQRS commands/queries, MediatR)
OrchestAI.Domain        → Core domain (entities, interfaces, enums, models)
OrchestAI.Infrastructure→ Integrations (agents, LLM, tools, EF Core, repos)
```

Dependency rule: outer layers depend inward. `Infrastructure` and `Application` both depend on `Domain`. `API` depends on `Application` and `Infrastructure` (for DI registration only).

---

## 2. Multi-Agent Architecture

```
IOrchestratorAgent
    └── OrchestratorAgent
            │  calls Anthropic API once (no tool loop)
            │  returns OrchestrationPlan { ExecutionMode, ExecutionOrder, AgentPrompts }
            │
            ▼
    StartOrchestrationHandler
            │
            ├── ExecutionMode.Parallel ──► Task.WhenAll(agents)
            │
            └── ExecutionMode.Sequential ─► foreach agent in ExecutionOrder
                                              prompt += prior agent output (≤3,000 chars)
                                              await agent.ExecuteAsync(...)

IAgent (per-agent instances via IAgentFactory)
    ├── ResearchAgent  → tools: perplexity_search, firecrawl_scrape
    ├── WriterAgent    → tools: file_write, firecrawl_scrape
    ├── CodeAgent      → tools: file_write
    ├── DataAgent      → tools: firecrawl_scrape
    └── BrowserAgent   → tools: (none, extensible)
```

Each `IAgent` runs an autonomous agentic loop via `AgentBase`:
- LLM call → if `stop_reason == "tool_use"` → invoke tool → append result → continue
- Max 10 iterations
- Loop exits on `end_turn` or iteration limit

---

## 3. CQRS Command Flow

### Create Task

```
POST /api/v1/tasks
  → CreateOrchestrationTaskCommand { UserId, Title, UserPrompt }
  → CreateOrchestrationTaskHandler
       → IOrchestrationTaskRepository.AddAsync(task)
       → returns CreateOrchestrationTaskResponse { Id, Status }
```

### Start Orchestration

```
POST /api/v1/tasks/{id}/start
  → StartOrchestrationCommand { TaskId }
  → StartOrchestrationHandler
       → validate task exists + is Pending
       → task.MarkRunning() → UpdateAsync
       → publish SSE: task_started
       → OrchestratorAgent.PlanAsync(userPrompt)
            → single LLM call → parse JSON → OrchestrationPlan
            → publish SSE: orchestrator_plan
       → branch on ExecutionMode
       → RunSubAgentAsync per agent
            → IAgentFactory.Create(agentType)
            → agent.ExecuteAsync(taskId, prompt)
       → aggregate results
       → task.MarkCompleted / MarkFailed → UpdateAsync
       → publish SSE: task_completed / task_failed
```

### Read Task

```
GET /api/v1/tasks/{id}?includeMessages=true&includeToolCalls=true
  → GetOrchestrationTaskQuery { TaskId, IncludeMessages, IncludeToolCalls }
  → GetOrchestrationTaskHandler
       → IOrchestrationTaskRepository.GetByIdAsync
       → maps to OrchestrationTaskDto
```

---

## 4. Sequential vs Parallel Execution

### Parallel (default)

All selected agents start simultaneously via `Task.WhenAll`. Use when agents are independent — e.g., Research + Code, where neither depends on the other's output.

```csharp
var tasks = plan.ExecutionOrder
    .Select(t => RunSubAgentAsync(taskId, t, plan.AgentPrompts[t], ct))
    .ToList();
var results = await Task.WhenAll(tasks);
```

### Sequential

Agents run one at a time in `ExecutionOrder`. Each agent receives the previous agent's output appended to its prompt (up to 3,000 characters, truncated with notice). Use when output of one agent is needed as input to the next.

```csharp
foreach (var agentType in plan.ExecutionOrder)
{
    var prompt = BuildSequentialPrompt(plan.AgentPrompts[agentType], priorOutput);
    var result = await RunSubAgentAsync(taskId, agentType, prompt, ct);
    if (result.Success) priorOutput = result.Output;
    // failure: log warning, continue without prior context injection
}
```

**Prompt injection format:**
```
{basePrompt}

--- Prior Agent Output ---
{priorOutput up to 3,000 chars}
[...truncated...]   ← appended only if truncated
```

**Failure policy:** continue-with-warning. A failed agent's output is not injected. Execution continues to the next agent with its base prompt. The task is marked failed at the end if any agent result has `Success = false`.

### How the Orchestrator decides

The `OrchestratorAgent` returns `execution_mode` in its JSON response:
- `"parallel"` → independent agents (default)
- `"sequential"` → one agent's output feeds the next

`execution_order` controls the run order for sequential mode. If absent, defaults to the `agents` array order.

---

## 5. MCP Tool Protocol

All tools implement `IMcpTool`:

```csharp
public interface IMcpTool
{
    string Name { get; }
    string Description { get; }
    ToolInputSchema InputSchema { get; }
    Task<McpToolResult> ExecuteAsync(JsonNode? input, CancellationToken ct);
}
```

`McpToolResult` is a value type: `(bool Success, string Output, string? ErrorMessage, int DurationMs)`.

Tools are registered in `IToolRegistry` (singleton, case-insensitive lookup). Each agent declares which tools it needs via `AvailableToolNames`. `AgentBase.BuildClaudeTool` converts `IMcpTool` → Anthropic SDK `CommonTool` for the LLM API call.

### Tool inventory

| Tool name | Class | External API |
|---|---|---|
| `perplexity_search` | `PerplexityTool` | api.perplexity.ai |
| `firecrawl_scrape` | `FirecrawlTool` | api.firecrawl.dev |
| `file_write` | `FileSystemTool` | local filesystem (sandboxed) |

`FileSystemTool` sandboxes all file operations to `./agent-workspace/` (configurable via `Tools__FileSystem__SandboxPath`). Path traversal attempts (e.g., `../../etc/passwd`) are rejected.

---

## 6. SSE Streaming

### Transport

`GET /api/v1/tasks/{id}/stream` opens a persistent `text/event-stream` HTTP response. The `TasksController` reads from `IOrchestrationEventBus` (an in-memory channel per task) and writes `data: {json}\n\n` lines.

```csharp
response.Headers.Append("Content-Type", "text/event-stream");
response.Headers.Append("Cache-Control", "no-cache");
response.Headers.Append("X-Accel-Buffering", "no"); // disables Nginx buffering
```

### Event Bus

`InMemoryOrchestrationEventBus` maintains a `ConcurrentDictionary<Guid, Channel<SseEvent>>`. Agents publish via `_eventBus.Publish(taskId, sseEvent)`. The SSE endpoint reads from the channel until `task_completed` or `task_failed` is published, then closes.

### React client

```javascript
const es = new EventSource(`${API_BASE}/tasks/${id}/stream`)
es.onmessage = (e) => {
    const data = JSON.parse(e.data)
    switch (data.event) { /* update UI state */ }
}
```

`VITE_API_URL` points directly to the Railway API — no Vercel proxy. This is critical: SSE through serverless proxies buffers responses, breaking real-time streaming.

---

## 7. Database Schema

Six tables, all managed via EF Core 8 migrations (PostgreSQL 15).

```
users
  id (uuid PK), email, created_at

orchestration_tasks
  id (uuid PK), user_id (FK), title, user_prompt, status (enum),
  final_result (text), total_input_tokens, total_output_tokens,
  total_cost_usd (decimal), created_at, updated_at

agent_executions
  id (uuid PK), task_id (FK), agent_type (enum), status (enum),
  input_tokens, output_tokens, cost_usd, error_message,
  created_at, updated_at

agent_messages
  id (uuid PK), execution_id (FK), role (enum), content (text),
  token_count, created_at

mcp_tool_calls
  id (uuid PK), execution_id (FK), tool_name, input_json (text),
  output_json (text), success (bool), duration_ms, created_at

cost_ledger
  id (uuid PK), execution_id (FK), model, input_tokens, output_tokens,
  cost_usd, created_at
```

`updated_at` is maintained automatically via `UpdatedAtInterceptor` (EF Core `ISaveChangesInterceptor`).

All DB access goes through repository interfaces (`IOrchestrationTaskRepository`, `IAgentExecutionRepository`, etc.). No direct `DbContext` usage outside `Infrastructure`.

---

## 8. Sequential Execution Algorithm

```
Input:  OrchestrationPlan { ExecutionOrder: [A, B, C], AgentPrompts: {...} }
Output: AgentExecutionResult[]

priorOutput ← null

for each agentType in ExecutionOrder:
    prompt ← AgentPrompts[agentType]
    if priorOutput is not null:
        prompt ← prompt + "\n\n--- Prior Agent Output ---\n" + truncate(priorOutput, 3000)
    
    result ← await RunSubAgentAsync(taskId, agentType, prompt, ct)
    
    if result.Success:
        priorOutput ← result.Output
    else:
        log Warning("agent failed, continuing without context injection")
        priorOutput ← null   ← failed output not propagated

    append result to results

return results
```

The 3,000 character limit prevents context window exhaustion when chaining agents that produce long outputs. The truncation marker `\n[...truncated...]` signals to the receiving agent that its context is incomplete.

---

## 9. Cost Accounting

Each LLM API call is tracked in `CostLedger`. Cost is calculated per completion:

```
costUsd = (inputTokens / 1_000_000 * inputPricePerMillion)
        + (outputTokens / 1_000_000 * outputPricePerMillion)
```

Pricing is configured in `appsettings.json` under `Pricing` keyed by model ID:

```json
"Pricing": {
  "claude-haiku-4-5-20251001": { "InputPerMillion": 0.80, "OutputPerMillion": 4.00 },
  "claude-sonnet-4-6":         { "InputPerMillion": 3.00, "OutputPerMillion": 15.00 }
}
```

`StartOrchestrationHandler` aggregates all agent results (including the orchestrator's own execution) and calls `task.AccumulateCost(totalInputTokens, totalOutputTokens, totalCostUsd)`.

The `task_completed` SSE event carries `totalCostUsd` which the React UI displays in the header.

---

## 10. Environment Variable Reference

### Backend (Railway)

| Variable | Description | Required |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL Npgsql connection string | Yes |
| `Anthropic__ApiKey` | Anthropic API key | Yes |
| `Tools__Firecrawl__ApiKey` | Firecrawl API key (web scraping) | For ResearchAgent |
| `Tools__Perplexity__ApiKey` | Perplexity API key (AI search) | For ResearchAgent |
| `ALLOWED_ORIGINS` | Comma-separated CORS origins | Yes |
| `PORT` | HTTP port (auto-set by Railway) | Auto |

### Frontend (Vercel)

| Variable | Description |
|---|---|
| `VITE_API_URL` | Railway API base URL (no trailing slash) |

### Local development

See `.env.example` at repo root. Docker Compose sets `POSTGRES_PASSWORD` and the API reads from `appsettings.json` by default.

---

## 11. Architectural Decisions

### ADR-001 — `IAnthropicClientWrapper` leaks SDK types into `AgentBase`

**Status:** Accepted (known debt)

`AgentBase` reads `MessageResponse.ToolCalls`, `.Content`, `.Usage`, `.StopReason` — all Anthropic.SDK types. Tests use reflection to construct `CommonFunction` instances with private setters, because the SDK only sets them during deserialization.

The correct fix is an `AgentTurn` domain record + ACL adapter in `IAnthropicClientWrapper`. Deferred until: (a) a second LLM provider is added, or (b) an SDK version upgrade breaks the reflection helper.

---

### ADR-002 — Sequential execution via prompt injection, not shared memory

**Status:** Accepted

Prior agent output is injected as plain text into the next agent's prompt rather than via a shared memory store (vector DB, Redis, etc.). This keeps the architecture stateless at the agent level, eliminates infrastructure dependencies, and is sufficient for chain lengths of 2-4 agents with outputs under the 3,000 character limit.

Trigger for revisiting: tasks that chain more than 4 agents, or prior outputs that exceed 3,000 characters regularly.

---

### ADR-003 — SSE via direct API calls (no Vercel proxy)

**Status:** Accepted

The React frontend calls the Railway API directly using `VITE_API_URL`. `vercel.json` contains only SPA routing rewrites — no `/api/*` proxy. Reason: Vercel's serverless edge network buffers streaming responses, which breaks real-time SSE. Direct calls eliminate buffering and simplify deployment.

CORS is configured in ASP.NET Core via `ALLOWED_ORIGINS` (comma-separated env var) rather than the proxy approach.
