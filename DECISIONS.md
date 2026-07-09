# Architecture Decision Records

## ADR-001: IAnthropicClientWrapper leaks SDK types into AgentBase
**Status:** Resolved (Week 5)  
**Context:** AgentBase read MessageResponse.ToolCalls, .Content, .Usage, .StopReason — 
all Anthropic.SDK types. Tests used reflection to construct CommonFunction instances with 
private setters.  
**Resolution:** Introduced `AgentTurn`/`AgentConversation` domain records and `ILlmProvider` 
as the ACL. `AgentBase` now only depends on `ILlmProviderFactory` + these domain types — zero 
SDK imports. `AnthropicProvider`, `AzureOpenAIProvider`, and `OpenAIProvider` each do their own 
SDK-specific mapping behind the interface. Agent-level tests (`AgentBaseProviderTests`) 
construct plain `AgentTurn` records with no reflection; SDK-specific reflection is now confined 
to the provider-boundary tests (`AnthropicProviderTests`) where it's unavoidable due to private 
SDK setters.

## ADR-002: Sequential execution via prompt injection, not shared memory
**Status:** Accepted  
**Context:** Prior agent output is injected as plain text (≤3,000 chars) into the next 
agent's prompt rather than via a shared memory store (vector DB, Redis). Keeps the 
architecture stateless at the agent level; eliminates infrastructure dependencies.  
**Trigger for revisiting:** Tasks chaining more than 4 agents, or prior outputs that 
regularly exceed 3,000 characters.

## ADR-003: SSE via direct API calls (no Vercel proxy)
**Status:** Accepted  
**Context:** The React frontend calls the Railway API directly using VITE_API_URL. 
vercel.json contains only SPA routing rewrites — no /api/* proxy. Vercel's serverless 
edge network buffers streaming responses, which breaks real-time SSE. Direct calls 
eliminate buffering and simplify CORS configuration (single ALLOWED_ORIGINS env var).  
**Trigger for revisiting:** Moving to a non-serverless frontend host that supports 
streaming proxies.

## ADR-004: IApprovalGateway is in-memory
**Status:** Accepted (known limitation)  
**Context:** `IApprovalGateway` uses a `SemaphoreSlim` per `taskId` in-memory 
(`InMemoryApprovalGateway`), the same pattern as `IOrchestrationEventBus`. Works for a single 
Railway instance. Will break with horizontal scaling — a signal from one instance won't reach 
a `StartOrchestrationHandler` blocked on another instance.  
**Trigger for fixing:** Running multiple Railway instances, or needing approval state to 
survive an API restart while a task is waiting.  
**Fix:** Replace with a Redis-backed distributed lock/signal (pub/sub `BRPOPLPUSH` or similar), 
mirroring whatever fix is chosen for `IOrchestrationEventBus`.

## ADR-005: Manager review is a second Orchestrator LLM call, not a separate agent
**Status:** Accepted  
**Context:** The Manager Agent review pass uses the same `OrchestratorAgent` class with a 
different system prompt (`ReviewSystemPrompt`) rather than introducing a `ManagerAgent` type. 
Avoids adding another `AgentType` enum value and keeps the six-agent roster unchanged. Review 
runs unconditionally after sub-agents finish (success or partial failure) so it can synthesize 
and quality-note failures rather than being skipped on any failure.  
**Trade-off:** A task with review enabled produces two `AgentExecution` rows with 
`AgentType = Orchestrator` — one for planning, one for review — distinguished only by SSE event 
ordering (`orchestrator_plan` vs `manager_review_started`/`completed`), not by a dedicated field.  
**Trigger for revisiting:** If the UI or API consumers need to query/filter review executions 
independently of planning executions, split into a dedicated `AgentType.Manager` with its own 
`AgentExecution` rows.

## ADR-006: Anthropic:ApiKey config path kept flat, not nested under Providers
**Status:** Accepted  
**Context:** New provider config (`AzureOpenAI`, `OpenAI`) was added as top-level sections 
rather than nesting all three providers under a single `Providers` section, so the existing 
`Anthropic:ApiKey` config path — and the `Anthropic__ApiKey` env var already set on the live 
Railway deployment — keeps working unchanged.  
**Trigger for revisiting:** A broader config reorganization pass where updating the Railway 
env var alongside the deploy is already planned.

## ADR-007: Memory extraction uses a separate LLM call
**Status:** Accepted  
**Context:** After each sub-agent execution, a lightweight second LLM call (capped at 512 
output tokens, same model/provider as the agent) extracts memorable facts from the output. 
This adds a small amount of cost and latency per agent execution. Extraction is best-effort — 
failures are logged and swallowed, never fail the agent's own result.  
**Trade-off:** Small, predictable cost increase for a genuine intelligence improvement over 
repeated sessions with the same user.  
**Trigger for replacing:** If memory extraction cost becomes significant at scale, switch to 
a local NLP model or a keyword-extraction heuristic instead of a second LLM round-trip.

## ADR-008: PII redaction is regex-based, not ML-based
**Status:** Accepted (known limitation)  
**Context:** `RegexPiiRedactor` covers common structured PII (email, phone, SSN, credit card) 
plus operator-defined custom regex rules. It does not catch unstructured PII (names, 
addresses, free-form context clues). Disabled by default (`PiiRedaction:Enabled=false`) so it 
never adds latency unless explicitly turned on.  
**Trade-off:** Fast, zero-cost, no external dependency. Misses contextual PII a human or an 
ML model would catch.  
**Trigger for replacing:** A healthcare or financial customer requires ML-based PII detection 
→ integrate Microsoft Presidio or Azure AI Language.

## ADR-009: Resume duplicates StartOrchestrationHandler's dispatch logic rather than sharing it
**Status:** Accepted (known duplication)  
**Context:** `ResumeOrchestrationTaskHandler` has its own copies of the sequential/parallel 
sub-agent dispatch loop and the review-and-finalize tail, instead of extracting a shared 
`IOrchestrationRunner` service used by both handlers. Extracting one would have required 
rewriting `StartOrchestrationHandlerTests`' extensive existing coverage to mock the shared 
service instead of `IAgentFactory`/`IOrchestratorAgent` directly.  
**Trade-off:** ~60 lines duplicated between the two handlers; a bug fix in one must be mirrored 
in the other.  
**Trigger for revisiting:** A third handler needing the same dispatch-and-finalize flow, or the 
next time either handler's dispatch logic needs a non-trivial change (do the extraction then, 
updating both test suites together).

## ADR-010: Checkpointing and memory scoped to sub-agents only, not Orchestrator plan/review
**Status:** Accepted  
**Context:** `TaskCheckpoint` writes and `AgentMemory` injection/extraction only happen in 
`AgentBase.ExecuteAsync` (the sub-agent tool loop), not in `RunLlmTurnAsync` (used by 
`OrchestratorAgent.PlanAsync`/`ReviewAsync`). The Orchestrator's routing JSON and review 
synthesis are meta-orchestration artifacts, not domain knowledge worth remembering or resuming 
independently — and checkpointing them would let `ResumeOrchestrationTaskHandler` skip 
re-running the review, which must always run fresh against whatever agents actually executed.  
**Trigger for revisiting:** If a future agent type's "planning" output becomes something worth 
resuming from independently (e.g. an expensive multi-step planning phase).

## ADR-011: Observability data model — hybrid aggregation, OpenTelemetry-shaped trace IDs
**Status:** Accepted  
*(Week 7 spec asked for "ADR-002" documenting the observability data model — ADR-002 was
already taken by the Week 4 sequential-execution decision above, so this uses the next
available number in the existing sequence instead of renumbering history.)*

### Investigation — what already exists vs. what's net-new
Before any schema work, inspected the existing checkpoint store, SSE pipeline, and retry
implementation, per the Week 7 spec's explicit instruction not to duplicate captured data:

- **SSE events are never persisted.** `InMemoryOrchestrationEventBus` is a pure in-process
  channel (`System.Threading.Channels`) — once a client disconnects or the event fires, it's
  gone. There is no `EventLog` table and never was one.
- **This doesn't matter, because the entity tables already ARE the raw trace.**
  `AgentExecution` (per-agent start/end/duration/tokens/cost), `AgentMessage` (per-turn,
  ordered), `McpToolCall` (per-tool-call, with `DurationMs` and success/failure), and
  `CostLedger` (per-agent-execution token/cost records) collectively capture everything the
  SSE stream announces live. The timeline view is a query over existing tables, not a new
  event-log table.
- **Real gaps**, confirmed by reading `AgentBase`, `AgentExecution`, `McpToolCall`,
  `CostLedger`, and their EF configurations:
  - Retry attempts fire `agent_retry` over SSE but write nothing to the database — no way to
    reconstruct "how many retries before success" after the fact.
  - `AgentExecution.ErrorMessage` / `McpToolCall.ErrorMessage` are raw exception-message
    strings with no structured category (timeout vs. provider error vs. validation vs. tool
    error) — the exception *type* is known at the point of failure and then thrown away.
  - No trace/span concept exists anywhere — `OrchestrationTaskId` / `AgentExecutionId` FKs
    give you containment, not a queryable parent-child span tree, and nothing marks
    `McpToolCall`s as children of the `AgentExecution` that issued them beyond the FK.
  - No memory-used / checkpoint-restored flags for the Execution Summary Card.
  - No pre-aggregated rollups — `CostLedger` is one row per agent-execution-cost-event; a
    "cost by day/week/month" dashboard query would mean scanning the full table on every load.
  - Pricing lives in `appsettings.json:Pricing` (an `IOptions`-bound dictionary) — not
    hardcoded in code, but not a queryable/admin-updatable table either, and changing it
    requires a Railway redeploy.
  - Token counts: confirmed both `AnthropicProvider` (`response.Usage.InputTokens/OutputTokens`)
    and `OpenAiChatMapper` used by Azure/OpenAI (`completion.Usage.InputTokenCount/
    OutputTokenCount`) already map real provider-reported usage, not estimates — this NFR
    needed no code change, just verification.

### Decision 1 — Aggregation strategy: hybrid, not pure read-time or pure write-time
Raw entities (`AgentExecution`, `McpToolCall`, `CostLedger`) remain the single source of
truth and stay queryable indefinitely for the timeline view — no raw-event table is
introduced. On top of that, a new `CostRollups` table holds one row per
(date, UserId, AgentType, Model) grain, populated by a background `IHostedService`
(`CostRollupAggregationService`) that ticks every 5 minutes and upserts the current day's
rollup from `CostLedger`, catching up any gapped days on startup.

**Query routing:** the cost dashboard reads `CostRollups` for any date range that doesn't
include today, and blends in a live aggregate query over today's `CostLedger` rows for the
current day. The execution timeline and summary card always read raw tables directly — a
single run's trace must be queryable the instant it completes, which is incompatible with a
5-minute-lag rollup.

**Why not pure read-time aggregation:** `CostLedger` will accumulate one row per
agent-execution-cost-event; dashboard queries doing `SUM()`/`GROUP BY` across weeks or months
of raw rows get slower as usage grows, and cost/error dashboards are exactly the queries that
get hit repeatedly by a human staring at a screen — they need to be fast every time, not just
on a cold cache.

**Why not pure write-time (no raw tables kept):** the timeline view's whole value proposition
is per-execution granularity — losing raw rows to save space would make it impossible to
answer "why did this specific run cost $4.20" after the fact, which is the core "what
happened, why, how to reproduce" promise this feature exists to keep.

**Eventual consistency is accepted for rollups** (rollups can lag up to 5 minutes) but never
for the raw trace of a just-completed run, matching the Week 7 NFR.

### Decision 2 — Trace ID scheme: intentionally OpenTelemetry-compatible
Every `OrchestrationTask` gets a `TraceId` (32 lowercase hex chars — OTel's 16-byte trace ID
format) generated at task-start. Every `AgentExecution` and `McpToolCall` gets a `SpanId`
(16 lowercase hex chars — OTel's 8-byte span ID format) plus a `ParentSpanId`. An
`AgentExecution`'s parent is the Orchestrator's planning span (the span that decided to
dispatch it); an `McpToolCall`'s parent is the `AgentExecution` span that invoked the tool.
Parent-child relationships are stored explicitly as columns, not reconstructed from
timestamps or FK containment after the fact — the Week 7 spec's explicit requirement.

IDs are generated via `System.Diagnostics.ActivitySource`/`Activity`
(`ActivityTraceId.CreateRandom()`, `ActivitySpanId.CreateRandom()`) rather than hand-rolled —
this is .NET's built-in, OTel-native tracing primitive, already spec-compliant by
construction, so there's no custom ID-generation code to get wrong.

**This is intentional, not incidental.** The alternative — reusing the existing GUID PKs
(`AgentExecution.Id`, `McpToolCall.Id`) directly as trace/span identifiers — would have been
less code today. The reason not to: this product's stated direction is "the LangSmith
equivalent for .NET," and every mature observability backend (Grafana Tempo, Azure Monitor,
Datadog, Honeycomb) speaks OTel trace/span IDs natively. Emitting OTel-shaped IDs from day one
means a future OTLP exporter is a mapping exercise, not a redesign — exactly the "one-time
decision, get it wrong and every later integration gets harder" the spec called out. The cost
of this choice is near zero: `ActivityTraceId`/`ActivitySpanId` are free BCL types, and the
extra columns are two indexed strings per row.

**Relationship to the existing PKs:** `TraceId`/`SpanId` are *correlation* identifiers for
cross-system tracing, not replacements for `Id`. Application code still looks up rows by
`AgentExecution.Id` everywhere it already does; `SpanId` exists purely for trace-tree assembly
and future OTel export.

### Decision 3 — Week 8 evaluation extensibility
`EvaluationResults` (Week 8) will FK to `AgentExecution.Id` (already a stable PK) and can
additionally carry `AgentExecution.SpanId`/`OrchestrationTask.TraceId` for direct correlation
with external observability backends once Week 8 ships. No schema change is needed on the
Week 7 tables to support this — the trace ID scheme decided above already covers it, which
was itself the point of deciding it now rather than after Week 8 needs it.

### New tables
- `CostRollups` — background-job-populated daily cost aggregates (Decision 1).
- `AgentRetryAttempts` — one row per retry, closing the "retry attempts aren't persisted" gap.
- `ModelPricing` — DB-backed replacement for `appsettings.json:Pricing`, seeded from the
  current config values, admin-updatable without a redeploy.

### Schema extensions (existing tables)
- `OrchestrationTask`: `+TraceId`, `+ResumedAt` (nullable — set when resumed from a checkpoint).
- `AgentExecution`: `+SpanId`, `+ParentSpanId`, `+ErrorCategory` (nullable), `+MemoriesInjectedCount`.
- `McpToolCall`: `+SpanId`, `+ParentSpanId`, `+ErrorCategory` (nullable).

### Retention
Raw execution data (`AgentExecution`, `AgentMessage`, `McpToolCall`, `CostLedger`,
`AgentRetryAttempts`) proposed at 30 days before archival/deletion — this is a genuinely
unbounded-growth table set otherwise. `CostRollups` (aggregates) kept indefinitely — they're
small (one row per day/user/agent/model) and are the long-term "cost over time" record once
raw detail ages out. **This is a decision needed, not yet implemented in Week 7** — no
deletion job ships this week; flagging it here per the spec's explicit instruction not to
leave it unbounded silently. Revisit before raw-table row counts become a real operational
concern (order of magnitude: tens of millions of rows).

**Trigger for revisiting Decision 1/2 together:** the first real OTLP exporter integration —
at that point, validate the `ActivityTraceId`/`ActivitySpanId` format assumptions against
whatever backend is targeted (Azure Monitor's OTel ingestion has its own quirks) before
building the exporter.

### Verified
Pre-commit pass measured query performance directly rather than trusting the design: seeded
a fresh 27-span multi-agent task (1 Orchestrator plan → 5 parallel sub-agents × 4 tool calls →
1 Orchestrator review; 3 levels of real parent-child nesting) plus 30 days × 5 agent types of
`CostRollups` data, then ran each query 10× through the real MediatR pipeline with `Stopwatch`.

| Query | Target | Median | Max |
|---|---|---|---|
| Execution timeline | <500ms | 2.8ms | 14.8ms |
| Cost dashboard (30d) | <1000ms | 3.5ms | 4.3ms |
| Comparison | <750ms | 2.0ms | 11.2ms |

The timeline query's N+1 status was checked empirically, not just by code inspection: Postgres
statement logging enabled, single query confirmed against the 27-span task — the LEFT JOIN
chain from `GetByIdWithExecutionsMessagesAndToolCallsAsync`'s `Include`/`ThenInclude`, not
one query per span.

## ADR-012: Evaluation & Scoring Data Model
**Status:** Accepted

### Investigation — what ADR-011 already decided vs. what's net-new
ADR-011 Decision 3 already anticipated this week: *"`EvaluationResults` (Week 8) will FK to
`AgentExecution.Id` (already a stable PK)."* Confirmed by reading `OrchestrationTask`,
`AgentExecution`, and their configurations: `TraceId` is an OTel-shaped correlation *string* on
`OrchestrationTask`, not a primary key — `OrchestrationTask.Id`/`AgentExecution.Id` are the
stable PKs. `EvalResult.AgentExecutionId` FKs to `AgentExecution.Id` (nullable, no enforced FK
relationship — see Decision 1), not a literal `TraceId` column, and this same FK shape is what
Week 9 post-hoc scoring will reuse against production `AgentExecution` rows.

Also confirmed during investigation: nothing in the LLM call pipeline (`AgentConversation`,
`AnthropicProvider`, the OpenAI-compatible mapper) supported pinning temperature before this
week — needed because the LLM judge scorer's reproducibility claim below would otherwise be
fiction. Added end-to-end (`AgentConversation.Temperature`, forwarded to both provider families).

### Decision 1 — Live-execution only this week; post-hoc scoring deferred to Week 9
`EvalResult.AgentExecutionId` is nullable and has no enforced EF `HasOne`/FK relationship (unlike
every other FK in this model). This is deliberate: enforcing referential integrity here would
assume every `EvalResult` traces back to an `AgentExecution` created *for* an eval run, which is
true this week (the background worker always creates a fresh `OrchestrationTask`/`AgentExecution`
per case) but stops being true the moment Week 9 adds post-hoc scoring of *production* traces —
those `AgentExecution` rows have no `EvalRunId` in their own ancestry at all, only an `EvalResult`
pointing at them from the outside. Building the loose reference now means Week 9 needs zero
schema migration to support it, exactly the "must work for both live-execution and future
post-hoc scoring" requirement from the Week 8 spec.

**Why not build post-hoc scoring now:** scoring an arbitrary historical `AgentExecution` needs a
case-selection mechanism (which past executions match which suite?) that doesn't exist yet and
would be guessed at, not designed, under this week's time box. Live-execution-only lets the
scoring/regression machinery ship and be exercised for real before that harder problem is solved.

### Decision 2 — Cost segregation: a `Source` discriminator, not a separate cost table
`CostLedger` gained `Source` (`Production`/`Eval`) and `EvalRunId` (nullable) rather than a
parallel `EvalCostLedger` table. Every dashboard/rollup query that reads `CostLedger` already
funnels through `ICostLedgerRepository.GetDailyAggregatesAsync` (confirmed by reading
`GetCostDashboardHandler` and `CostRollupBackgroundService` — both call only this one method), so
adding one `Where(c => c.Source == CostSource.Production)` clause there is the single choke point
that keeps eval cost out of both the live "today" dashboard view and the background rollup job,
with no risk of a second code path forgetting to filter.

`EvalRunId` on `CostLedger` (and separately on `AgentExecution`) is threaded through
`IAgent.ExecuteAsync`'s new optional `evalRunId` parameter, set only by the eval background
worker. `CostLedger.OrchestrationTaskId` remains `NOT NULL`, so the LLM judge scorer's own cost
row reuses the `OrchestrationTaskId` of the case invocation it's scoring (carried via
`EvalScoringContext`) rather than requiring a schema change to make it nullable.

### Decision 3 — LLM judge non-determinism is an accepted limitation, not a bug
`LlmJudgeScorer` pins every judge call to `Temperature = 0` (`AgentConversation.Temperature`,
forwarded to both `Anthropic.SDK.Messaging.MessageParameters.Temperature` and
`OpenAI.Chat.ChatCompletionOptions.Temperature`). This makes judge scores **directionally
reliable across runs — not bit-exact reproducible**. Temperature 0 sharply reduces sampling
variance but neither provider guarantees byte-identical output at temperature 0 (model updates,
provider-side batching/routing, and floating-point non-associativity across hardware all remain
possible sources of drift). Regression detection therefore compares scores against
`EvalCase.RegressionThreshold`, a tolerance band, rather than asserting exact equality — the
threshold isn't just a UX nicety, it's structurally required by this limitation.

### Decision 4 — Baseline promotion is manual and explicit this week
`EvalRun.BaselineRunId` is set only when the caller of `RunEvalSuiteCommand` passes one — there is
no "most recently completed run" auto-selection. `GetRegressionReportQuery` throws a
`ValidationException` (not an empty/zeroed report) when `BaselineRunId` is null, per the spec's
explicit requirement that a missing baseline fail loudly. Auto-selecting a baseline is deferred
as a future policy decision once there's real usage data on how teams actually want to pick one
(most recent completed run? most recent run on a specific branch? a pinned "golden" run?) —
guessing at that policy now, with zero usage data, is exactly the kind of decision the Week 8
spec says not to make prematurely.

### Decision 5 — Running a suite costs real money and can hit rate limits (acknowledged, not solved)
Every eval-suite run is real agent invocations against real LLM providers, at real provider cost
(tracked and segregated per Decision 2, but not free) — plus the LLM judge's own call, for
`LlmJudge`-scored cases. Running a suite frequently (e.g., on every commit) will incur real cost
and, at high enough frequency or suite size, real rate-limit pressure against the same provider
credentials production traffic uses. Nothing in this week's design throttles, batches, or queues
eval runs against a shared rate-limit budget — `EvalRunBackgroundWorker` processes one run's cases
without any concurrency cap or backoff coordination with production traffic. This is a known,
accepted gap for Week 8, flagged here so it isn't a surprise later, not a problem solved by
anything in this plan.

### New tables
- `EvalSuites`, `EvalCases`, `EvalRuns`, `EvalResults` — see Tasks 4–6 for full shape.

### Schema extensions (existing tables)
- `CostLedger`: `+Source` (`CostSource`, default `Production`), `+EvalRunId` (nullable).
- `AgentExecution`: `+EvalRunId` (nullable) — the trace/execution record itself, not just the
  cost row, so "which traces came from eval run X" is answerable independent of cost data.

### Trigger for revisiting
- The first Week 9 post-hoc-scoring implementation — validate that `EvalResult.AgentExecutionId`
  being a loose, unenforced reference (Decision 1) is still the right call once a real
  case-selection mechanism for historical executions exists.
- The first time eval run frequency causes a real rate-limit incident against production traffic
  (Decision 5) — that's the trigger to design throttling, not before.
- The first time someone asks for automatic baseline selection with a specific policy in mind
  (Decision 4).
