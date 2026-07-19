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
**Status: Decided (updated pre-Week-10).** Raw telemetry — `AgentExecution`, `AgentMessage`,
`McpToolCall`, `AgentRetryAttempts`, `CostLedger`, and the Week 8/9 eval tables (`EvalRun`,
`EvalResult`) — is retained **indefinitely** during the pre-adoption development phase (no
external tenants yet, single-operator usage). No deletion/archival job ships as part of this
decision; this replaces the earlier "30 days, undecided" placeholder from Week 7 with an
explicit choice not to build retention machinery before there's a real reason to.

**Trigger to revisit (either condition, whichever comes first):**
1. Raw-table row counts approach the order of magnitude flagged in Week 7 — tens of millions of
   rows — where unindexed growth starts affecting query latency or storage cost materially.
2. The first external tenant is onboarded. Multi-tenant retention has different legal/cost
   pressures (data-deletion requests, per-tenant storage attribution) than single-operator
   development data, so the policy should be redesigned at that point, not extended as-is.

`CostRollups` (and any future daily/aggregate rollup tables) remain retained indefinitely
**regardless of which trigger fires** — they're small (one row per day/user/agent/model), and
are the long-term "cost/quality over time" record precisely because raw detail is expected to
eventually age out from under them.

**Why decide now instead of building the deletion job:** guessing at a retention window (30
days? 90?) with zero usage data or tenant-model clarity would be the same kind of premature
policy commitment ADR-012 Decision 4 explicitly avoided for baseline auto-selection — the
correct trigger conditions are now defined, but the actual archival/deletion mechanism is
deferred until one of them fires, at which point real data volume and tenancy shape will
inform the design instead of speculation.

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

## ADR-013: Post-Hoc Scoring

**Status:** Accepted

### Investigation — what ADR-012 already anticipated
ADR-012 Decision 1 deliberately left `EvalResult.AgentExecutionId` nullable with no enforced FK,
specifically so post-hoc scoring of production `AgentExecution` rows would need zero schema
migration on that column. Confirmed true: no change to `AgentExecutionId` was needed this week.
Everything else — `EvalResult.EvalCaseId` being non-nullable, `EvalRun.SuiteId` being
non-nullable, `IEvalScorer.ScoreAsync` requiring a full `EvalCase` — was net-new work.

### Confirmation #1 — `EvalResult.EvalCaseId` nullability
Confirmed by reading `EvalResult.cs` and `EvalResultConfiguration.cs`: `EvalCaseId` was `Guid`
(non-nullable, `.IsRequired()`, unique-indexed with `EvalRunId`). Migrated to `Guid?`. A null
`EvalCaseId` now means "this result came from post-hoc scoring against a rubric, not a
predefined case" — the same signal `GetScoredAgentExecutionIdsAsync` (Task 3) uses to scope its
idempotency lookup to post-hoc-origin rows only.

### Confirmation #2 — Post-hoc scoring is judge-only
`RuleBasedScorer` requires a machine-checkable `ExpectedCriteria` (`ExactMatch`/`Regex`/
`JsonSchema`) authored against a specific predefined case's expected output. Arbitrary historical
production traces have no such predefined expectation — inventing one after the fact would be
guessing at what the trace *should* have produced, not scoring what it *did* produce. `LlmJudge`,
by contrast, already only reads a free-text `rubric` from `ExpectedCriteria` and was designed
around "does this satisfy a general standard," which is exactly what post-hoc grading needs
("was the tool call appropriate," "was the output well-formed").

**Decision:** `RequestPostHocScoringCommand` rejects any `ScorerType` other than `LlmJudge` with
a `ValidationException` naming this ADR. Rather than changing `IEvalScorer.ScoreAsync`'s
signature (which would touch both scorers and every existing Week 8 test), a new
`EvalCase.CreateEphemeral(rubric, passThreshold)` factory builds a transient, never-persisted
`EvalCase` carrying the same `{"rubric": ..., "passThreshold": ...}` JSON shape `LlmJudgeScorer`
already parses — so the interface and the scorer are reused completely unchanged.

### Confirmation #3 — Idempotency, plus a distinct, deliberate re-score path
Two behaviors, not one, per the spec's explicit either/or: **default** is skip — re-running a
post-hoc request against the same trace with the same scorer + `scorer_version` is a no-op for
that trace, counted in `EvalRun.SkippedAlreadyScoredCount`. **Deliberate override** is
`RequestPostHocScoringCommand.ForceRescore` (bool, default `false`) — without this, there would
be no way to correct a bad judge score (e.g. a flaky judge call, or a prompt tweak that didn't
bump `LlmJudgeScorer.Version`) short of manual DB surgery.

The two paths cannot share the same enforcement mechanism naively: the idempotency backstop is a
**database-level unique index** on `EvalResults (AgentExecutionId, ScorerType, ScorerVersion)
WHERE EvalCaseId IS NULL`. If `ForceRescore` just skipped the app-level pre-check and inserted a
second row for an already-scored trace, that insert would violate the index and throw. So
`ForceRescore` is defined as **supersede, not append**: `EvalRunBackgroundWorker.ProcessPostHocRunAsync`,
when `run.ForceRescore` is true, does not consult `GetScoredAgentExecutionIdsAsync` at all (it
doesn't need to know what's already scored — it's rescoring everything in scope regardless), and
before each insert calls the new `IEvalResultRepository.DeletePostHocResultAsync(agentExecutionId,
scorerType, scorerVersion)` to remove any prior result for that exact tuple first. This keeps the
unique index a real, always-enforced invariant — "at most one *current* post-hoc result per
trace+scorer+version" — instead of relaxing it, and avoids double-counting a re-scored trace in
`GetPostHocScoringSummaryQuery`'s pass rate (Task 6).

`ForceRescore` is a first-class `EvalRun` column (not buried in `SelectionCriteriaJson`) because
it's a behavior switch the worker branches on, not descriptive audit metadata.

### Confirmation #4 — Bounding cost exposure
Two enforcement layers, both required: (1) `EvalOptions.MaxPostHocTracesPerRequestCeiling`
(default 500) is a hard, config-level ceiling no single request's `MaxTraces` can exceed; (2)
`RequestPostHocScoringHandler` requires either an explicit date range or an explicit trace-ID
list — `AgentType` alone is rejected as a sole filter because it doesn't bound the result set in
time. If the actual match count exceeds the caller's `MaxTraces`, the handler throws rather than
silently scoring only the first `MaxTraces` matches — a silent truncation would let a caller
believe they scored "last month's traffic" when they actually scored an arbitrary time-ordered
slice of it.

### Decision 5 — Trace selection resolves to a concrete ID list at request time, not lazily at worker time
`RequestPostHocScoringHandler` calls `IAgentExecutionRepository.SelectForPostHocScoringAsync`
once, and persists the *resolved* `AgentExecutionId` list on `EvalRun.SelectionCriteriaJson` —
the background worker never re-runs the date-range/agent-type query itself. This makes a given
`EvalRunId`'s scope reproducible (re-processing the same run after a crash/restart always touches
the same traces) and keeps the `MaxTraces` cap enforcement a single choke point, rather than a
check that could pass at request time and then drift if new production traces land in the same
date range before the worker drains the queue.

### Decision 6 — `EvalRun.Source` discriminator, not a separate run table
Added `EvalRunSource { LiveSuite, PostHoc }` to `EvalRun` and made `SuiteId` nullable — same
"discriminator column, not a parallel table" shape as `CostLedger.Source` from ADR-012 Decision
2, for the same reason: every consumer of `EvalRun` (background worker, regression report,
results/summary queries) already reads through the same repository methods, so one `Source`
check at each of those call sites is the single place that needs to know the difference, instead
of two full parallel schemas that could drift.

`GetRegressionReportQuery` explicitly rejects `Source != LiveSuite` runs — a regression report
diffs against a baseline run, and post-hoc runs have neither a suite nor a baseline concept this
week (see non-goals).

### Decision 7 — Post-hoc scorer type is implicit, not stored per-run
Because confirmation #2 makes post-hoc scoring always `LlmJudge` this week, `EvalRun` does not
store a per-run scorer type — `ProcessPostHocRunAsync` resolves `EvalScorerType.LlmJudge`
directly. **Trigger for revisiting:** the first time a second post-hoc-eligible scorer type is
added — at that point `EvalRun` needs its own scorer-type column rather than a hardcoded
assumption.

### Implementation notes
Two real deviations surfaced during implementation that this ADR's design didn't anticipate,
both plumbing rather than a change to any decision above.

`RequestPostHocScoringHandler` (Task 4) needs `EvalOptions.MaxPostHocTracesPerRequestCeiling`
(Confirmation #4) from inside `OrchestAI.Application`, but `EvalOptions` originally lived in
`OrchestAI.Infrastructure.Configuration` — the only existing consumer, `LlmJudgeScorer`, is
itself inside Infrastructure, so the type had never needed to cross the Application/Infrastructure
boundary before. Reading all four `.csproj` files confirmed this codebase's actual dependency
graph has `Application` and `Infrastructure` as siblings that both depend on `Domain` and never on
each other; making the handler compile as originally drafted would have required a first-of-its-
kind `Application → Infrastructure` reference, inverting this project's Clean Architecture
layering. Instead, `EvalOptions` was relocated to `OrchestAI.Application.Configuration`, and a new
`Infrastructure → Application` project reference was added in its place — non-cyclic
(`Domain ← Application ← Infrastructure ← API` remains a valid DAG) and consistent with the
direction API already depends on both. `LlmJudgeScorer` and its tests were updated to the new
namespace; no scoring or validation logic changed.

Separately, Task 9's frontend post-hoc scoring form submits `scorerType`/`agentType` as JSON
strings (`"LlmJudge"`, `"Research"`), but `OrchestAI.API`'s `Program.cs` registered
`AddControllers()` with no JSON options configured, and System.Text.Json's default enum handling
deserializes only from the numeric underlying value. Every real HTTP request the frontend could
send would therefore fail model binding with a `JsonException` before ever reaching the MediatR
handler — invisible to this week's (and last week's) unit tests, which construct C# command
objects directly and never round-trip through JSON. The fix was a global
`JsonStringEnumConverter` registered on `AddControllers().AddJsonOptions(...)`, chosen over a
per-enum `[JsonConverter]` attribute so every current and future enum-typed request/response
field is covered uniformly. This also transparently fixed an identical, previously-latent bug on
`AddEvalCaseRequest.ScorerType` in `EvalsController`, which had the same defect but had never been
exercised with a string-valued `scorerType` over real HTTP. `JsonStringEnumConverter`'s default
constructor still accepts raw integer values on deserialize, so the change is additive and safe
against every existing frontend consumer of enum-typed fields (all already treat them as strings).

### Trigger for revisiting
- The first time `ForceRescore` is used against a large trace set — today it re-scores every
  resolved trace unconditionally (no partial "only rescore the ones that changed" mode); revisit
  if that becomes a real cost concern once there's usage data.
- The first post-hoc-eligible `IEvalScorer` other than `LlmJudge` (Decision 7).
- The first real post-hoc batch large enough that `MaxPostHocTracesPerRequestCeiling`'s default
  of 500 becomes a genuine throughput bottleneck rather than a safety rail.

## ADR-014: Tenant Identity and Isolation

**Status:** Accepted

### Investigation — what already existed vs. what's net-new
Confirmed by reading every entity in `src/OrchestAI.Domain/Entities/` in full: 13 entities
already carried an ownership chain back to a `User` (directly or transitively via
`OrchestrationTask`/`AgentExecution`), but `EvalSuite`/`EvalCase`/`EvalRun`/`EvalResult` had
**zero** ownership concept — every "user" implicitly shared one global eval-suite space since
Week 8. Zero authentication of any kind existed (`Program.cs` has no `UseAuthentication()`/
`UseAuthorization()`, confirmed by full read); every `UserId` in this codebase was a plain,
unvalidated `Guid` supplied by the caller. `User` itself carries no auth/tenant field —
deliberately left untouched this week; `Tenant` is a new, separate identity layer, not a
retrofit of `User`.

### Confirmation #1 — Tenant definition
One `Tenant` = one external org/customer; `ApiKey` is many-to-one to `Tenant` (revoking a key
never orphans data). `User` stays an internal actor label, orthogonal to `Tenant` — not part of
the auth chain this week. Introducing a `User`-belongs-to-`Tenant` relationship, or any
within-tenant RBAC, is explicitly deferred (non-goal).

### Confirmation #2 — Tenant-scoped tables
13 entities implement a new `ITenantScoped` marker interface: `OrchestrationTask`,
`AgentExecution`, `AgentMemory`, `AgentMessage`, `AgentRetryAttempt`, `CostLedger`, `CostRollup`,
`McpToolCall`, `TaskCheckpoint`, `EvalSuite`, `EvalCase`, `EvalRun`, `EvalResult`. `ModelPricing`
and `User` remain global/shared, `Tenant`/`ApiKey` are the identity layer itself (also not
`ITenantScoped` — there is no "current tenant" to scope a tenant lookup to before one is
resolved). `EvalSuite`/`EvalCase`/`EvalRun`/`EvalResult` becoming tenant-private is a deliberate,
user-visible behavior change from Week 8-9: eval suites are no longer implicitly global.

All 13 tables are FK-constrained to `Tenants` (`ON DELETE RESTRICT`), not merely indexed — a
tenant row can never be deleted while it still has referencing data in any of these tables. This
took two migrations, not one: `AddTenantIsolation` (Task 6) added the `TenantId` columns, backfill,
and the `ApiKeys`-specific `CK_ApiKeys_TenantId_NotDefault` check constraint, but did not configure
the `HasOne(Tenant)` relationship on the 13 existing tables' EF configurations — the original plan
draft described these tables as "FK-constrained" but Task 2's entity/config changes never actually
added that relationship. This gap was caught during Task 6's review and escalated for a human
decision (add the real FKs vs. document the gap and defer); the decision was to add them, landing
as a second, immediately-following migration, `AddTenantForeignKeys`.

### Confirmation #3 — Centralized enforcement, reads and writes, plus explicit relationship checks
EF Core global query filters, applied **generically** via reflection over every entity
implementing `ITenantScoped` in `AppDbContext.OnModelCreating` — a future entity that implements
the interface is protected automatically, with no per-entity `HasQueryFilter` call to remember.
Writes are enforced by a new `TenantScopingInterceptor` (mirroring the existing
`UpdatedAtInterceptor` pattern exactly): it stamps `TenantId` on every new `ITenantScoped` entity
from the ambient tenant, and rejects (never silently overwrites) any entity that somehow already
carries a mismatched `TenantId`. No domain `Create(...)` factory accepts a `TenantId` parameter
(the two named exceptions are `ApiKey.Create` and `CostRollup.Create` — see the note below
Confirmation #5b) — this closes the "client-supplied `TenantId`" attack surface at the design
level, not just at a runtime check.

The filter/interceptor pair is the *default*, not the *only*, mechanism: `RunEvalSuiteCommand`'s
`BaselineRunId` needed an explicit new ownership lookup (the handler never looked the referenced
run up at all before this week), while `ResumeOrchestrationTaskCommand`'s `TaskId` and
`RequestPostHocScoringCommand`'s explicit `TraceIds` turned out to already be correctly handled
by the filter alone (a foreign ID is either invisible — 404 — or silently excluded from a
resolved set, consistent with how these commands already treat "not visible" elsewhere). Each of
these three was verified individually against the actual current handler code, not assumed.

**Known, accepted limitation — disconnected-`Update()` cannot detect a tampered `TenantId`.**
`TenantScopingInterceptor`'s write-side tamper check compares `OriginalValue != CurrentValue` on
the `Modified` branch (not EF's `IsModified` flag — see Implementation notes below for why). This
correctly catches an in-context tampering attempt (an entity fetched and mutated within the same
`DbContext`), which is what `TenantScopingInterceptorTests.SaveChanges_ExistingEntity_TenantIdCannotBeChanged`
exercises. It is a structural no-op, however, for this codebase's standard repository pattern —
fetch in one `DbContext` (via `IDbContextFactory`), then `ctx.Set<T>().Update(entity)` in a fresh
one — because EF seeds `OriginalValue` from whatever `CurrentValue` already is on the CLR object at
attach time, not from the real database row, so the two can never differ regardless of what
`TenantId` was set to before `Update()` was called. This is accepted as safe today only because
`ITenantScoped.TenantId` has a private setter on every implementing entity with no code path that
mutates it post-construction (the only writers are `TenantScopingInterceptor` itself and the two
named `Create(...)` exceptions above, neither of which flows through the disconnected-`Update()`
path) — not because the disconnected-update path independently re-verifies this at runtime. Pinned
down by `TenantScopingInterceptorTests.SaveChanges_DetachedEntityWithTamperedTenantId_DoesNotThrow_DocumentingKnownLimitation`
as a deliberate, tested gap rather than an unnoticed one. Adding a public/internal setter to any
`ITenantScoped.TenantId`, or a new factory/mutation path that sets it post-construction, would
silently reopen this gap — see the code comments on `TenantScopingInterceptor.cs` and
`ITenantScoped.cs` before touching either.

### Confirmation #4 — Fail closed
The filter is `e.TenantId == accessor.TenantId`, comparing a non-nullable `Guid` against a
`Guid?`. With no ambient tenant, `accessor.TenantId` is `null`, and SQL's `x = NULL` is never
`TRUE` — reads return zero rows with no special-casing, and critically, no `||` fallback branch
was written that could flip this fail-open. The interceptor explicitly throws
`TenantContextViolationException` on any write with no resolved tenant. The default/system
tenant (`00000000-0000-0000-0000-000000000001`, created by the `AddTenantIsolation` migration for
backfill only) has zero `ApiKey` rows, ever — there is no code path that could mint one for it,
since key issuance always requires an explicit, already-existing `TenantId` argument.

### Confirmation #5 — Background propagation
`TenantId` travels explicitly with queued work: `EvalRunQueueItem(Guid EvalRunId, Guid TenantId)`
replaces the old bare-`Guid` queue payload. The handlers don't need a new dependency to supply
this — by the time `RunEvalSuiteHandler`/`RequestPostHocScoringHandler` call
`_runRepository.AddAsync(run, ct)`, `TenantScopingInterceptor` has already stamped `run.TenantId`
from the ambient tenant (set by the auth middleware for the whole HTTP request), and because the
interceptor sets it via `entry.Property(...).CurrentValue`, the in-memory entity reflects the
stamped value too — the handler just reads `run.TenantId` straight off the entity.
`EvalRunBackgroundWorker.ExecuteAsync` restores the ambient tenant scope from the dequeued item
**before** `ProcessRunAsync` touches any tenant-scoped repository (its very first call fetches
the `EvalRun` itself, which is `ITenantScoped`), and checks the owning tenant's status
(`Active`/`Suspended`/deleted) before doing any further work — a tenant suspended after enqueue
gets its queued run marked `Failed` with a clear reason, never silently completed.

### Confirmation #5b — Cost rollup: a deliberate, narrow, audited exception
`CostRollupBackgroundService` aggregates across every tenant by nature — it cannot run inside
any single tenant's ambient scope. A new `ICurrentTenantAccessor.BeginSystemWriteScope()` (a
second, independent `AsyncLocal<bool>` flag) is the **only** bypass: while active,
`TenantScopingInterceptor` skips its normal auto-stamp/reject logic entirely, and three repository
methods explicitly call `IgnoreQueryFilters()`, each guarded by an `InvalidOperationException` if
called outside a system-write scope, so an accidental future call from tenant-facing code fails
loudly instead of silently leaking cross-tenant data: `ICostLedgerRepository.
GetDailyAggregatesForRollupAsync`, `ICostRollupRepository.UpsertAsync`, and
`ICostRollupRepository.GetLastRolledUpDateAsync`. `CostRollups` gained `TenantId` in its grouping
key (`Date, TenantId, UserId, AgentType, Model`, both in the entity and in the unique index) so two
tenants' costs are never collapsed into one aggregate row or into one colliding unique-index slot.
Auditable by construction: grep for `IgnoreQueryFilters`/`BeginSystemWriteScope` and confirm each
has exactly the call sites described here (Task 14's audit).

**Two named exceptions to "no factory ever takes `TenantId`," not one:** `CostRollup.Create(...)`
(this task) and `ApiKey.Create(...)` (Task 1) both accept an explicit `tenantId` parameter — the
review that surfaced this during Task 1's implementation initially read as a contradiction of the
Global Constraints until traced to its actual justification (see Task 1's code comment on
`ApiKey.Create` and the Global Constraints entry above). Both share the same shape: a trusted
writer with no ambient ITenantScoped write path to bypass. `CostRollup`'s writer
(`CostRollupBackgroundService`) derives `TenantId` from an authoritative SQL join
(`OrchestrationTask.TenantId`), never from a caller. `ApiKey`'s writer (`CreateApiKeyHandler`,
admin-secret-gated, Task 8) has no ambient tenant scope during the call at all — the operator
explicitly designating the tenant for a new key IS the operation. Neither is reachable from a
tenant-authenticated request path, which is the actual property the constraint protects. Any
*other* factory method accepting `TenantId` is a defect, not a third instance of this pattern —
`TenantScopingCompletenessTests` (Task 13) and this ADR are what a future reviewer checks before
accepting a claimed third exception.

### Confirmation #6 — Ambient tenant mechanism
`ICurrentTenantAccessor`, backed by `AsyncLocal<Guid?>` (plus the separate `AsyncLocal<bool>` for
system-write scope). Chosen over `IHttpContextAccessor`/DI-scope alignment because it must serve
both an HTTP request (set once by auth middleware) and a background-worker job (set explicitly
per dequeued item) identically — `IHttpContextAccessor` doesn't exist in the latter, and
`AppDbContext` instances are created per-call via `IDbContextFactory`, which resolves
constructor dependencies from an internal per-call scope rather than the ambient HTTP request
scope. `AsyncLocal` flows correctly across async continuations within whichever logical call
chain is executing, independent of DI scope boundaries — proven directly by
`AsyncLocalCurrentTenantAccessorTests`'s concurrent-flows test (two simultaneous `Task.Run`
bodies, each setting a different tenant, never observe each other's value).

### Confirmation #7 — API key format
`orch_live_<publicKeyId>.<secret>` — a 12-character random base62 `publicKeyId` (indexed lookup
key) and a 32-character random base62 `secret` (≈190 bits of entropy). Hashed with SHA-256, not
a slow KDF (bcrypt/argon2 exist to resist brute-forcing a *low-entropy human-chosen* password —
this is a long, randomly-generated machine credential, which that threat model doesn't apply
to). Verified via `CryptographicOperations.FixedTimeEquals`, never a raw string comparison, which
can leak timing information proportional to how many leading bytes match. The raw key is
returned to the caller exactly once, at creation, and is never persisted, logged, or retrievable
again — only `HashedSecret` is stored.

### Confirmation #8 — Backfill and provisioning bootstrap
One well-known default/system tenant (`00000000-0000-0000-0000-000000000001`, `Tenant.DefaultTenantId`),
created by the `AddTenantIsolation` migration, with zero `ApiKey` rows ever — structurally
unauthenticatable (confirmation #4), enforced at two independent layers rather than one:
`CreateApiKeyHandler` rejects the ID explicitly at the application layer, and a Postgres `CHECK`
constraint (`CK_ApiKeys_TenantId_NotDefault` on the `ApiKeys` table, Task 6) refuses the row at
the database layer regardless of how the insert was attempted — a raw SQL statement, a future
code path that forgets the handler-level check, or a bug in the handler itself all still fail.
Convention alone ("no code path currently does this") was judged insufficient for the one
invariant fail-closed enforcement itself depends on; a schema constraint doesn't erode as the
codebase changes. `CreateTenantCommand`/`CreateApiKeyCommand`/`RevokeApiKeyCommand` are reachable
only through `AdminController`, gated by `RequireAdminSecretFilter` (a static, separately
configured `Admin:BootstrapSecret`, checked via constant-time comparison, never a tenant API
key) — this is deliberately not the same auth path as Task 9's tenant middleware, since an
ordinary tenant must never be able to create another tenant or mint itself unlimited keys.

### Confirmation #9 — Suspension
Invalid/missing/revoked/expired key → `401`. Valid key, `Suspended` tenant → `403`. Queued work
for a tenant suspended after enqueue is rejected (marked `Failed` with a clear reason) when the
worker checks status, before any further processing — never silently completed. Verified directly
against real `TenantAuthenticationMiddleware`/`EvalRunBackgroundWorker` code, not assumed.

### Confirmation #10 — Frontend authentication transition
Temporary, explicitly non-production: an in-memory-only (module-scoped JS variable, never
`localStorage`/`sessionStorage`, never baked into the build) API-key prompt, gating the app's
render and injecting `Authorization: Bearer <key>` via a thin `authenticatedFetch` wrapper. This
avoids *persistence*-based exposure (nothing survives a refresh or ships in a build artifact) —
it does **not** avoid *runtime* exposure: any JavaScript-accessible value, including one held
only in memory, remains readable by an XSS vulnerability during an active session. A real
production design needs a backend-for-frontend session, short-lived tokens, or httpOnly cookies
— not a long-lived machine credential living in browser JS at all. CORS (`Program.cs`) already
has no `.AllowCredentials()` and explicit allowed origins (unchanged this week) — relevant since
a cookie-based future redesign would need to revisit this.

### Confirmation #11 — Frontend stays internal/demo-only, bugs A/B fixed anyway
The React frontend remains an internal development/demo playground — no further product investment (production session auth, self-serve tenant/API-key onboarding, UX redesign) is scheduled at this time. Two correctness bugs blocking the demo's baseline function were fixed regardless: Task 1 (EventSource cannot send `Authorization` headers, requiring a browser-compatible ticket mechanism) and Task 2 (orchestrator-planning failures left tasks stuck at `Running` instead of failing), both described in the Phase 1 plan's investigation (`docs/superpowers/plans/2026-07-19-phase1-architecture-product-validation.md`). These are independent-of-product-status baseline blockers needed for any demo to function. Revisit only if a future milestone explicitly calls for a public-facing demo (hiring conversation, real customer demo, actual end-user requirement) — not proactively.

### Decision 11 — `EvalSuite`/`EvalCase`/`EvalRun`/`EvalResult` becoming tenant-private is a deliberate behavior change
Before this week, every eval suite was implicitly shared across all callers (there was no
isolation concept at all). After this week, each suite belongs to exactly one tenant, and a
different tenant cannot see or run it. This is called out explicitly because it's the one
tenant-scoping decision that changes *product* behavior, not just adds a security boundary
around already-private data — flagged here so it isn't a surprise to whoever operates multiple
tenants' eval suites going forward.

### Implementation notes
Four real deviations surfaced during implementation that this ADR's design didn't anticipate.

**`TenantScopingInterceptor` trusted `IsModified`, which is always `true` for this codebase's
disconnected-`Update()` pattern (Task 11).** Every repository (`EvalRunRepository`,
`OrchestrationTaskRepository`, `AgentExecutionRepository`, `ApiKeyRepository`, ...) uses
`IDbContextFactory` — fetch in one `DbContext`, mutate the CLR object, then
`ctx.Set<T>().Update(entity)` in a fresh one. EF Core's `Update()` on a previously-untracked graph
marks every scalar property `IsModified = true`, including `TenantId`, even when its value is
identical to what's already persisted. The interceptor was trusting that flag, so it would have
rejected every legitimate status transition (`EvalRun.MarkRunning/Completed/Failed`,
`OrchestrationTask` transitions, etc.) on any `ITenantScoped` entity the instant a real tenant
context was involved — a production-breaking bug that no test caught before Task 11, since
handler tests mock repositories and the one pre-existing integration test never wired the
interceptor in. Fixed by comparing `OriginalValue != CurrentValue` instead (see Confirmation #3's
"Known, accepted limitation" note above for the residual gap this fix carries and why it's safe).

**`CostRollupRepository` bypassed `IgnoreQueryFilters()` and the unique index lacked `TenantId`
(Task 12).** Initially, `UpsertAsync`'s existing-row lookup and `GetLastRolledUpDateAsync` queried
`ctx.CostRollups` without `IgnoreQueryFilters()`, while running inside `BeginSystemWriteScope()`
(ambient `TenantId` is `null` there). The fail-closed filter made every "does this already exist"
check return zero rows unconditionally, so every rollup tick after the first would have attempted
a second `INSERT` for an already-rolled-up day and crashed on the unique-constraint violation.
Compounding this, the unique index itself was `(Date, UserId, AgentType, Model)` — no `TenantId`
— creating a real cross-tenant collision risk via the shared `DatabaseSeeder.EvalSystemUserId`
constant (multiple tenants' rollups could legitimately share that same `UserId`). Both fixed in
the same task: `IgnoreQueryFilters()` plus the system-write-scope guard added to both methods
(mirroring the pattern `CostLedgerRepository.GetDailyAggregatesForRollupAsync` already used), and
the unique index extended to `(Date, TenantId, UserId, AgentType, Model)` via the
`ExtendCostRollupUniqueIndexWithTenantId` migration, verified against real Postgres (the InMemory
provider doesn't enforce unique indexes at all, so this required a real-database integration
test). Confirmation #5b above describes the resulting final state only.

**Task 14's raw-data-access audit found 3 pre-existing `ExecuteDeleteAsync` call sites the
original plan's investigation had missed.** `TaskCheckpointRepository.DeleteByTaskIdAsync` and
`AgentMemoryRepository.DeleteAsync` predate Week 10 and were judged safe by construction — both
filter on a tenant-unique key (`taskId`/`id` respectively), so they cannot cross a tenant boundary
regardless of whether `ExecuteDelete` also honors the query filter. `AgentMemoryRepository.
DeleteExpiredAsync` filters only on `ExpiresAt` — not tenant-unique — so it had genuine
cross-tenant risk if `ExecuteDelete` didn't apply the global filter the way a normal query does.
Proven safe empirically rather than assumed from documentation:
`TenantFilterExecuteDeleteTests.DeleteExpiredAsync_ScopedToTenantA_NeverDeletesTenantBsExpiredRows`
runs against real Postgres (EF Core's InMemory provider cannot translate `ExecuteDelete` at all)
and confirms EF Core 8+ applies the global query filter to `ExecuteDelete` for this project's
actual model/filter configuration, consistent with (but independently verified beyond) EF Core's
documented behavior. No production code changes were needed — every audited call site was already
safe.

**`RequireAdminSecretFilter` and `TenantAuthenticationMiddleware` were relocated from
`Infrastructure` to `API` during review (Tasks 8-9).** The original task briefs specified
`src/OrchestAI.Infrastructure/`, matching where most cross-cutting Infrastructure code lives, but
both types are ASP.NET Core pipeline glue (`IAsyncActionFilter` / `IMiddleware`) that pulls in
`Microsoft.AspNetCore.Mvc`/`Microsoft.AspNetCore.Http` — framework references a plain persistence/
cross-cutting class library shouldn't carry. Corrected to live in `src/OrchestAI.API/` (Filters/
Middleware) instead. Two new `LayeringTests` guardrails were added specifically because the
existing `Infrastructure_DoesNotDependOnApi` check would not have caught this class of mistake
(neither type referenced `OrchestAI.API` directly, only ASP.NET Core MVC/Http types):
`Infrastructure_DoesNotDependOnAspNetCoreMvc` and `Infrastructure_DoesNotDependOnAspNetCoreHttp`.
See `DESIGN_PRINCIPLES.md` for the general principle this established.

### Trigger for revisiting
- The first time a `User`-within-`Tenant` concept (multiple named users per tenant, with
  RBAC) is needed — `User` and `Tenant` are deliberately orthogonal this week; revisit once
  there's a real multi-seat-per-tenant requirement.
- The first time self-service tenant/key management is needed — today it's operator-only via
  `AdminController`; revisit once there's a real reason a tenant needs to rotate its own keys
  without an operator.
- The first time the frontend auth flow needs to survive a page refresh or be used by a
  non-technical end user — the in-memory prompt (confirmation #10) is deliberately not that;
  revisit with a real backend-for-frontend session design at that point.
- The first time `CostRollupBackgroundService`'s system-write-scope pattern is needed by a
  second cross-tenant job — at that point, confirm the bypass is still narrow and auditable with
  two call sites instead of one, not three or four with divergent justifications.
- The first time a public/internal setter is proposed for any `ITenantScoped.TenantId`, or a new
  factory/mutation path that sets it post-construction — revisit the disconnected-`Update()`
  known limitation (Confirmation #3) before accepting either change.

## ADR-015: Abuse and Cost Protection

**Status:** Accepted

### Investigation — what already existed vs. what's net-new
Before this week, ADR-014's tenant isolation was purely a data-visibility boundary: any
authenticated tenant could enqueue unlimited concurrent `OrchestrationTask`s, each triggering
unbounded parallel sub-agent fan-out and unbounded per-agent tool-call loops, with no
request-rate ceiling, no per-task structural cap, and no tenant-level running-cost check at
admission time. `ITenantLimitsProvider`, `TaskAdmissionReservation`, `RejectionEvent`, and every
enforcement point below are net-new this week. ADR-011's hybrid `CostRollup` + live-`CostLedger`
read pattern and ADR-014's tenant-scoping machinery (`ICurrentTenantAccessor`, `ITenantScoped`,
`TenantScopingInterceptor`) already existed and are reused unchanged throughout this design, not
rebuilt.

### Confirmation #1 — Rate-limiting algorithm: token bucket, tenant-partitioned
`Microsoft.AspNetCore.RateLimiting`/`System.Threading.RateLimiting` (ships in the ASP.NET Core 8
shared framework — no new NuGet package). Token bucket over sliding/fixed window because it
allows a legitimate short burst (e.g. a dashboard loading several widgets in one page load)
while still enforcing a steady average rate — a fixed/sliding window would reject that same
legitimate burst outright. Task 9's design note, quoted directly: *"`TokenLimit = TokensPerPeriod
= RequestsPerMinute`, `ReplenishmentPeriod = 1 minute`, `QueueLimit = 0` (reject immediately
rather than queue — queueing inside the rate limiter itself would add latency this is supposed to
prevent, not just cap volume)."* One `GlobalLimiter` (not per-endpoint `[EnableRateLimiting]`
policies) applies uniformly to every `api/v1/*` route except `/health`, `/swagger`,
`/api/v1/admin/*`, and any path ending `/stream` (the SSE endpoint — a long-lived connection
doesn't fit request-rate semantics). Partition key is the ambient tenant ID
(`ICurrentTenantAccessor.TenantId`), read via `ITenantLimitsProvider.GetSnapshot` — the
synchronous, cache-only path built specifically because `RateLimitPartition`'s factory delegate
must be synchronous.

### Confirmation #2 — In-memory/single-instance state is an accepted limitation, not an oversight
Three independent pieces of enforcement state all live in single-process memory: the rate
limiter's `PartitionedRateLimiter` (Task 9), `ITaskToolCallBudget`'s `AsyncLocal` running counter
(Task 8), and `IEvalRunQueue`'s per-tenant depth `ConcurrentDictionary` (Task 10). None of the
three is backed by Redis or any shared store. This is explicitly out of scope for this week — a
horizontally-scaled deployment (more than one Railway/API instance) would let a tenant's
requests, tool calls, or queue depth spread across instances that don't share state, silently
defeating each limit. **Trigger to revisit:** the first time a second API instance is deployed —
see "Trigger for revisiting" below.

### Confirmation #3 — The two-part cost-cap model
**(a) Per-task structural ceiling**, enforced two different ways for two different reasons.
`MaxAgentsPerTask` is checked once, pre-dispatch, because it's knowable in advance;
`MaxToolCallsPerTask` is a running counter checked per call, because it isn't. Task 8's design
note, quoted directly: *"`OrchestrationPlan.SelectedAgents` is known the instant `PlanAsync`
returns, so `MaxAgentsPerTask` is checked once, pre-dispatch, in `StartOrchestrationHandler` — the
cleanest 'reject before any work happens' case. Tool calls are never known in advance
(`OrchestrationPlan` carries no tool-call count) — they happen one at a time, deep inside each
sub-agent's own agentic loop (`AgentBase.InvokeToolAsync`), and sub-agents run in parallel via
`Task.WhenAll`. `MaxToolCallsPerTask` is therefore a single, shared, task-wide running counter
(`ITaskToolCallBudget`, `AsyncLocal`-backed exactly like `ICurrentTenantAccessor`) that every
sub-agent increments atomically (`Interlocked`) as it makes calls — the first call that would
exceed the cap throws `AgentCapExceededException`, which `AgentBase.ExecuteAsync`'s *existing*
catch-all turns into a normal Failed `AgentExecutionResult`."*

**(b) Tenant-level running budget** (Task 6): checked via atomic reservation at admission time
against `actual spend (CostRollup + live CostLedger, ADR-011's hybrid pattern) + SUM(active,
non-stale TaskAdmissionReservation rows)`. The spend read itself is a fast snapshot, deliberately
outside the atomic transaction — only the reservation math (concurrency count, budget check,
reservation insert) is atomic; re-reading `CostRollup`/`CostLedger` inside a `SELECT ... FOR
UPDATE`-held transaction would hold that lock far longer than necessary.

### Confirmation #4 — The atomic-reservation mechanism
`IOrchestrationAdmissionRepository.TryAdmitAsync` takes a `SELECT ... FOR UPDATE` row lock on the
tenant's own `Tenants` row, serializing concurrent admissions **per tenant** (never cross-tenant),
inside one explicit DB transaction that also performs the `Pending -> Running` CAS
(`ExecuteUpdateAsync`) and the concurrency/budget checks — all-or-nothing. Confirmed against real
concurrent Postgres transactions (not a single shared/rolled-back transaction) by
`AdmissionConcurrencyRaceTests` (Task 11), including a test that specifically requires the second
transaction to observe the first's committed reservation. Task 6's design note, quoted directly —
the load-bearing architectural justification for why this is one repository method, not a
composition of smaller ones: *"the all-or-nothing guarantee (brainstorming clarification #1) only
holds if the tenant row lock, the task-state CAS, the concurrency count, the budget check, and the
reservation insert all share exactly one DB transaction. Splitting this across multiple
`IDbContextFactory`-per-call repository methods (this codebase's normal pattern) would silently
break that guarantee — each call would get its own connection/transaction. This is a deliberate,
narrow exception to the usual repository-per-entity convention, justified the same way Week 10
justified `TenantScopingInterceptor`'s system-write-scope: one documented, auditable exception to
a normal rule, not a pattern to repeat casually elsewhere."*

### Confirmation #5 — Reservations vs. the immutable cost ledger
`DESIGN_PRINCIPLES.md`'s "Operational state vs. audit state" section (added this week, not scoped
to Week 11 alone) states the general rule: *"Operational state exists only to support the
system's current behavior (reservations, rate-limiter counters, concurrency slots, queue depth,
caches). It is ephemeral, may be reconstructed or discarded after failures, and should never
become part of the permanent historical record. Audit state records what actually happened during
execution ... Audit state is immutable, durable, and must never be rewritten or derived from
operational state."*

Applied concretely here: `TaskAdmissionReservation` is pure operational state. Task 5's design
note: it is *"never read by, written into, or derived from `CostLedger`/`CostRollup`"* —
`ReleaseAsync` is a hard `DELETE`, never an adjustment written anywhere else, and `CostLedger`/
`CostRollup` remain the untouched, immutable audit trail exactly as ADR-011 established them.

**Crash-recovery TTL, stated explicitly as the accepted single-instance-architecture limitation it
is:** a reservation whose owning task crashes mid-execution (so Task 7's `finally` never runs) is
never released — it physically remains in the table — but Task 6's admission math excludes any
reservation older than `AbuseProtectionOptions.ReservationStalenessMinutes` (default 30 minutes)
from its count/sum, so it stops affecting future admissions once stale. No reconciliation service
exists or is planned this week to delete these orphaned rows; see "Trigger for revisiting" below.

### Confirmation #6 — The unified response contract, and its one deliberate asymmetry
Every synchronous-HTTP-request rejection returns `429` + `Retry-After` + a
`{reason, detail, ...}` JSON body, built by one shared builder, `RejectionResponder`, called from
exactly two entry points: the rate limiter's `OnRejected` callback (`RateLimited`) and
`TenantLimitExceededExceptionHandler` (`ConcurrencyExceeded`/`BudgetExceeded`/
`QueueBackpressure`). `RejectionEvent` is written at the same choke point in both cases, so every
rejection is independently queryable via `GET /api/v1/rejections` regardless of which enforcement
point produced it.

`AgentCapExceeded` does **not** go through this HTTP contract at all — deliberately, not as an
inconsistency. Task 8's design note covers the mechanics: the cap-exceeded exception is caught
internally by `AgentBase.ExecuteAsync`'s existing catch-all (or, for `MaxAgentsPerTask`, handled
inline in `StartOrchestrationHandler`), producing a normal Failed `AgentExecutionResult`/task
status rather than propagating as an HTTP exception. The product reasoning: by the time an agent
cap can be evaluated, the caller has already received their `202 Accepted` for `POST
/tasks/{id}/start` — dispatch runs detached, in the background, with no HTTP response in flight to
write a `429` to. `AgentCapExceeded` is instead surfaced via task status (`Failed`) + SSE
(`task_failed`) + an independently-written `RejectionEvent`, so it remains observable through the
same `GET /api/v1/rejections` list as every HTTP-contract rejection, just via a different
delivery mechanism.

### Confirmation #7 — Reject, never truncate, on any orchestrator-level cap
Both halves of Confirmation #3(a) fail the task cleanly — `Failed` status, a specific error
message, and a `RejectionEvent` — rather than silently truncating a plan (dispatching fewer agents
than planned) or dropping a tool call's result and pretending success.
`StartOrchestrationAgentCapTests` encodes this as an explicit assertion: exceeding
`MaxAgentsPerTask` must fail the task **before dispatching a single agent**, never a partial
dispatch. This is the single most important reject-vs-truncate justification in the whole design,
restated here because it's this brief's own stated rationale: a partially-executed task that
looks `Completed` to the eval/scoring layer is a worse failure mode than a task that's visibly
`Failed`. A silently-truncated plan would corrupt exactly the kind of "what happened, why, how to
reproduce" trace ADR-011's timeline view exists to preserve, and would let an eval run silently
score a task against work it never actually did.

### Confirmation #8 — Idempotency-key TTL and behavior
Idempotency applies to `POST /tasks` only, not `/start` — `/start`'s existing `Pending`-state
guard, hardened to the atomic `Pending -> Running` CAS in Task 6, already makes a retried
`/start` call safe (either `409`, or a genuinely-still-`Pending` task starts exactly once)
without a second idempotency mechanism. Default TTL is 24 hours
(`AbuseProtectionOptions.IdempotencyKeyTtlHours`); a repeated key with a mismatched payload
returns `409 Conflict`.

**Concurrent-first-use race — handled, not merely accepted.** Task 4's original design note
proposed treating this as an accepted scope limit: *"A known, accepted scope limit: two genuinely
*concurrent* (not sequential-retry) `POST /tasks` calls with the same brand-new key could both
pass the 'not found' check before either inserts, racing past the unique `(TenantId,
IdempotencyKey)` index into a raw `DbUpdateException` — acceptable here (unlike the budget/
admission race) because the failure mode is 'one extra valid task created,' not a security or
budget-overshoot problem."* Task 4's own code review found this prediction wrong: the actual
failure mode was an **unhandled 500**, not "one extra valid task" — a real Important-severity bug,
not a harmless tradeoff, because the loser's caller received an error instead of any task at all.

This was fixed in commit `eaee0e8`, not left as designed. `IdempotencyRecordRepository.AddAsync`
now catches the unique-index violation (`DbUpdateException` wrapping a `PostgresException` with
`SqlState: "23505"` and `ConstraintName ==
"IX_IdempotencyRecords_TenantId_IdempotencyKey"`) and throws a typed
`IdempotencyKeyConflictException` carrying the winning record's `TaskId`.
`CreateOrchestrationTaskHandler` catches that exception around its own `AddAsync` call and returns
the **winning task's response to the losing caller** — both concurrent callers get back the same
task ID, exactly like the sequential-replay path. The loser's own `OrchestrationTask` row (created
earlier in the same `Handle` call, before the race was discovered) is left behind as an orphaned
but harmless `Pending` row — an internal artifact, never surfaced to any caller and never returned
by any API response. This behavior is verified by real-Postgres integration tests (EF Core's
InMemory provider does not enforce unique indexes), not by unit tests alone. This is a deliberate
contrast with Confirmation #4's admission race, which required true atomicity because its failure
mode (budget/concurrency overshoot) is materially worse than an internal orphaned row.

### Decision — Deliberate deviations from the brief's literal domain-model field list
Three deviations from the plan's literal field list, all resolved during Task 1 rather than
discovered later:

- **`TenantLimits.MaxQueueDepth`** was added to the entity/`ResolvedTenantLimits` even though the
  originating plan's enforcement-points list required per-tenant queue-depth limits without
  listing the corresponding `TenantLimits` field — added at implementation time so Task 10's
  queue backpressure would have a real per-tenant value to read rather than a hardcoded constant.
- **`TenantLimits.Create(tenantId, ...)`** is a third named exception to ADR-014's "no
  `ITenantScoped` factory takes `TenantId`" rule — same shape as `ApiKey.Create`/
  `CostRollup.Create` (ADR-014 Confirmation #5b): admin-only, reachable only through
  `AdminController`, with no ambient tenant scope to bypass. Documented directly in
  `TenantLimits.cs`'s code comment at the point of definition, not discovered after the fact.
- **`AbuseProtectionOptions`** lives in `OrchestAI.Application.Configuration`, not
  `Infrastructure.Configuration` — proactively avoiding the exact `Application`-depends-on-
  `Infrastructure` layering violation ADR-013's `EvalOptions` relocation already fixed reactively
  in Week 9. `Application`-layer handlers (`CreateOrchestrationTaskHandler`'s idempotency TTL,
  Task 6's admission handler's reservation staleness) need to read this options type directly, and
  `LayeringTests.Application_DoesNotDependOnInfrastructure` would fail if it lived in
  Infrastructure. `TenantLimitsDefaults` stays in `Infrastructure.Configuration` since only
  `EfTenantLimitsProvider` (Infrastructure) reads it directly — the two options types are
  deliberately split across layers by who actually consumes them, not lumped into one file for
  convenience.

### Implementation notes
Nine real deviations surfaced during implementation (Tasks 1-11, plus a final whole-branch review
finding fixed afterward) that this ADR's design didn't anticipate. (Task 7's review gate running
late due to a mid-session machine restart is a process note, not a design deviation, and is
omitted here — see `.superpowers/sdd/progress.md`.)

**Task 1's brief specified an `EfTenantLimitsProvider` constructor that fails DI validation at
startup.** The brief's literal constructor took `ITenantLimitsRepository` directly into a
`Singleton`-registered provider. Running `dotnet ef migrations add` (which builds and validates
the full DI container) failed with `Cannot consume scoped service
'OrchestAI.Domain.Interfaces.ITenantLimitsRepository' from singleton
'OrchestAI.Domain.Interfaces.ITenantLimitsProvider'` — a hard ASP.NET Core captive-dependency
failure, not a design-time-only artifact; the brief's literal code produces a container that
cannot start. Fixed by switching the constructor to `IServiceScopeFactory`, resolving
`ITenantLimitsRepository` from a fresh `IServiceScope` on each cache-miss — mirroring the existing
`ModelPricingCache` (Week 7) convention for this exact "Singleton cache needs a Scoped repository"
shape. Verified end-to-end by starting the real API against live Postgres, not just by unit test.
A separate post-review fix added a per-tenant `SemaphoreSlim` single-flight guard to
`EfTenantLimitsProvider.GetAsync` (mirroring `ModelPricingCache.RefreshAsync`'s double-checked-
locking shape, adapted to a per-tenant lock instead of one global lock) to prevent a thundering
herd of concurrent cache-miss DB reads for the same tenant.

**Task 4 found and fixed a Critical-severity deterministic bug and an Important-severity race bug
in the idempotency-key unique index, both in commit `eaee0e8`.** The Critical bug: expired
`IdempotencyRecord` rows were correctly filtered out on read but never deleted, so reusing any key
after its TTL expired collided with the stale row on the real unique `(TenantId, IdempotencyKey)`
index and threw an unhandled `DbUpdateException` (500) on every single reuse — 100% deterministic,
not a race. The Important bug is the concurrent-first-use race described in Confirmation #8 above.
Both are fixed by the same mechanism (`IdempotencyRecordRepository.AddAsync` catching the unique-
violation and either deleting-and-retrying for an expired row, or throwing a typed
`IdempotencyKeyConflictException` for a live conflict) and verified via real-Postgres integration
tests, since EF Core's InMemory provider does not enforce unique indexes.

**Task 6 discovered the brief's shared-rolled-back-transaction test-harness pattern is
structurally incompatible with a repository that opens its own internal transaction.**
`OrchestrationAdmissionRepository.TryAdmitAsync` is the codebase's first repository method to call
`ctx.Database.BeginTransactionAsync()` itself; the brief's literal test harness
(`TransactionScopedDbContextFactory`, one shared connection/transaction rolled back at the end)
made every test fail identically with `The connection is already in a transaction and cannot
participate in another transaction` — EF Core's `RelationalConnection.EnsureNoTransactions()`
unconditionally rejects a second `BeginTransactionAsync` on a connection already enlisted via
`UseTransaction()`. This is not a case a savepoint API could route around; the repository
genuinely needs its own top-level transaction to prove real row-lock and rollback semantics. Fixed
by building a new `RealDbContextFactory` harness — genuinely independent physical connections per
`CreateDbContext()` call, mirroring production's `AddDbContextFactory` registration, with explicit
per-test cleanup in a `finally` block instead of relying on an outer rollback. This is directly
relevant to Confirmation #4's claim of verification via "real concurrent Postgres transactions" —
that claim is true, but achieving it required this structural test-harness discovery, not just
following the brief as written. The same fix had to be reapplied to Task 11's tests later, which
needed the identical independent-connection pattern.

**Task 8's brief undercounted its own Files list by 9.** The original plan's file list missed
`OrchestratorAgent.cs` as a 6th `AgentBase` subclass needing the `ITaskToolCallBudget`/
`IRejectionEventRepository` constructor parameters (the plan's investigation had only enumerated
5 concrete agents), and missed 7 pre-existing test call sites broken by the new constructor
params. Independent review confirmed the fix was mechanical and applied identically everywhere;
one of the 9 extra files (`StartOrchestrationReservationReleaseTests.cs`) was itself missed in the
implementer's own self-audit of the fix, though the code change there was correct.

**Task 9's own exempt-path test was tautological.** The brief's Step-1 test
(`BuildGlobalLimiter_ExemptPath_NeverRejects`) used a `null` tenantId, which meant it exercised
the unrelated "no ambient tenant" fallback branch rather than the `IsExemptPath` logic it claimed
to test — inherited from the brief itself, not implementer-introduced. Fixed by adding a second
test with a real tenantId and `requestsPerMinute=1` against an exempt path (100 requests, all must
succeed), with a masking-proof (neutralize `IsExemptPath`, confirm the new test fails; restore,
confirm it passes) verified genuine by independent re-review. The original tautological test was
left in place as harmless, brief-mandated coverage.

**Task 10's 2-file scope grew to include mechanical fixes to 2 pre-existing integration test
files.** `CrossTenantBackgroundFlowIntegrationTests.cs` and `PostHocScoringIntegrationTests.cs`
construct `InMemoryEvalRunQueue` directly and broke once its constructor gained the
`ITenantLimitsProvider` parameter — the same shape as Task 8's `OrchestratorAgent` surprise.
Review confirmed both fixes are behavior-preserving (a permissive `maxQueueDepth:1000` mock;
neither test exercises queue-depth/backpressure, an orthogonal concern) and that a full-tree grep
for `new InMemoryEvalRunQueue` found no other missed caller.

**Task 11 found and fixed a real, deterministic FK-ordering bug in the brief's own literal
`CleanUpAsync` test code.** The original helper deleted `Users` (via a subquery over
`OrchestrationTasks`) before deleting `OrchestrationTasks` itself; since
`FK_OrchestrationTasks_Users_UserId` runs in `Restrict` mode with `OrchestrationTasks` as the
child, that ordering threw a `23503 foreign_key_violation` on every single run — reproduced
identically 5/5 times, i.e. deterministic, not flaky. Fixed to match the delete-order convention
already established by `OrchestrationAdmissionRepositoryTests.CleanupAsync` in the same directory
(delete `OrchestrationTasks` before `Users`), verified across 13 consecutive post-fix runs (39/39
executions, zero flakiness). 24 leaked `Tenant` rows and 36 leaked `OrchestrationTask` rows from
the pre-fix failed runs were found and manually cleaned, with zero residue confirmed
independently by both the implementer and the controller.

**A real, still-open gap: no integration test drives `AgentBase.InvokeToolAsync`'s
`MaxToolCallsPerTask` budget-check end-to-end.** `AsyncLocalTaskToolCallBudgetTests` (Task 8)
proves the counter/scope mechanism in isolation, and `StartOrchestrationAgentCapTests` proves the
`MaxAgentsPerTask` pre-dispatch check, but nothing exercises the `RejectionEvent` write +
`AgentCapExceededException` propagation to a Failed `AgentExecutionResult`/`OrchestrationTask`
through a real `AgentBase.InvokeToolAsync` call. This gap was identified during Task 8's own
implementation, re-confirmed during Task 8's review, and is tracked as the deferred Task 13
follow-up (already committed at `1fa1fac`) rather than silently treated as covered by this ADR.

**Task 13 is now closed.** `AgentToolCallCapIntegrationTests` drives a real `ResearchAgent`
(and, for the parallel case, a real `CodeAgent`) with a real `AsyncLocalTaskToolCallBudget`
through the real `StartOrchestrationHandler.Handle` — only the `ILlmProvider` and leaf
`IMcpTool`s are faked. The primary test configures a cap of 2 against a single turn requesting 3
tool calls and asserts the `OrchestrationTask` ends `Failed` (never `Completed`) with the exact
`AgentBase.cs:601` wording surfacing all the way up through the persisted `AgentExecution` and
`OrchestrationTask.ErrorMessage`, a `RejectionEvent` (`Reason == AgentCapExceeded`) was
persisted, and the underlying tool was invoked exactly twice (not three times), proving the
budget check runs before tool invocation rather than after. A masking-proof (temporarily
short-circuiting the cap-exceeded branch in `AgentBase.InvokeToolAsync`, confirming both new
tests fail, then reverting) confirmed the tests exercise the real mechanism rather than passing
vacuously — with the cap disabled, both fake agents instead ran to `MaxAgenticIterations`,
producing 60 tool invocations instead of the capped 4, which incidentally also demonstrates the
cap's role as a runaway-loop guard. The second test configures `ExecutionMode.Parallel` with two
concurrently-dispatched agents (via the real `Task.WhenAll` in `Handle`) sharing one budget
scope, each requesting 3 tool calls against a shared cap of 4 (combined demand 6), with an
artificial `Task.Delay` in each fake tool to force genuine thread-pool interleaving rather than
synchronous continuation chaining; it asserts total successful invocations across both agents is
exactly the cap (never more, never fewer) regardless of interleaving order, extending
`AsyncLocalTaskToolCallBudgetTests.TryIncrement_ConcurrentIncrementsWithinOneScope_
NeverExceedsCapAcrossParallelCallers` (Task 8, direct `TryIncrement()` calls) to prove the same
guarantee holds when driven through real, concurrent agent execution. Full suite: 428/428
(up from 426 after Tasks 1-2). Added in this commit.

**A final whole-branch review found Confirmation #1's `ITenantLimitsProvider.GetSnapshot` handoff
to `RateLimiterSetup.BuildGlobalLimiter` has two distinct bugs, only one of which this ADR's
Task 9 design anticipated and only one of which is fixed here.**
`System.Threading.RateLimiting.PartitionedRateLimiter.Create`'s partition-key resolver runs on
every `AttemptAcquire`, but the `RateLimitPartition.GetTokenBucketLimiter` factory nested inside
it — the thing that actually bakes `TokenLimit`/`TokensPerPeriod` into a real
`TokenBucketRateLimiter` — only ever runs once per partition key, the first time that key is seen,
for the life of the process.

*Bug #1 — cold-cache-at-first-request (FIXED in this commit).* Nothing warmed
`EfTenantLimitsProvider`'s cache before the rate limiter ran, so a tenant's very first request (or
a tenant that only ever calls read-only endpoints — dashboard, `GET /tasks`, `GET /rejections` —
none of which previously touched `GetAsync`) hit `GetSnapshot()` with a cache miss, got back
`TenantLimitsDefaults.RequestsPerMinute` (120 req/min), and that became the tenant's bucket
configuration permanently — a later cache warm from any other call site (admission, dispatch,
enqueue) had no effect on the already-created bucket. Fixed by calling
`ITenantLimitsProvider.GetAsync(tenant.Id, context.RequestAborted)` in
`TenantAuthenticationMiddleware.InvokeAsync` (`src/OrchestAI.API/Middleware/
TenantAuthenticationMiddleware.cs`) immediately after the tenant is resolved and confirmed
`Active`, before `next(context)` is invoked — i.e. before `UseRateLimiter()` runs, since
`TenantAuthenticationMiddleware` is registered ahead of `UseRateLimiter()` in `Program.cs`. Every
tenant's `GetSnapshot()` call inside the rate limiter's partition-key resolver is now guaranteed to
see a warm cache on that tenant's first request, cold-cache or not. Proven by
`TenantLimitsCacheWarmUpIntegrationTests.ColdCacheTenant_FirstRequestThroughMiddleware_
RateLimiterEnforcesConfiguredLimit_NotSystemDefault`, which deliberately does not pre-warm the
cache before the limiter runs — it drives the real middleware (with a genuine, non-mocked
`EfTenantLimitsProvider`) through `InvokeAsync` and only builds the tenant's bucket, via
`RateLimiterSetup.BuildGlobalLimiter()`, from inside `next`, reproducing the actual pipeline order.

*Bug #2 — bucket immutability after a limits change (ACCEPTED, NAMED ARCHITECTURAL LIMITATION —
same treatment as Confirmation #2's in-memory/single-instance state; NOT fixed here).* Warming the
cache earlier does nothing for a tenant whose bucket already exists. An admin `PUT .../limits` call
that changes `RequestsPerMinute` via `SetTenantLimitsCommand` has zero effect on that tenant's live
rate limiting until the process restarts — which drops every tenant's buckets, not just the
changed one. This is not a staleness problem cache-warming could ever fix: `GetSnapshot` genuinely
is re-read on every request (proven, not assumed — see below), but `PartitionedRateLimiter` simply
never reconsiders an already-existing partition's bucket regardless of what that read returns. This
is a structural limitation of `System.Threading.RateLimiting`'s partition-caching model as used
here, deliberately not attempted in this fix. Demonstrated empirically by
`RateLimiterPartitioningTests.BuildGlobalLimiter_TenantLimitsChangedAfterBucketCreated_
ChangeHasNoEffectOnExistingBucket`: it builds a real limiter, exhausts a tenant's bucket at
`RequestsPerMinute=3`, changes the mocked `ITenantLimitsProvider`'s live return value to 100
(simulating a landed admin change), and shows the next request is still rejected — while also
asserting `GetSnapshot` genuinely was called again and genuinely did return 100, ruling out "stale
read" as the explanation. Future fix: partition-key versioning — key partitions by
`{tenantId}:{limitsVersion}` (a version stamp `SetTenantLimitsCommand` increments on every update)
instead of `{tenantId}` alone, so a limits change produces a fresh partition/bucket and the stale
one simply ages out of use, rather than requiring in-place mutation of an existing
`TokenBucketRateLimiter`. Real follow-up work, not attempted this commit.

### Trigger for revisiting
- The first time a second API instance is deployed — the in-memory rate limiter, tool-call-budget
  counter, and queue-depth state (Confirmation #2) all need to become shared/distributed (Redis or
  similar) at that point, not before.
- The first time `AssumedCostPerToolCallUsd`'s flat-rate estimate proves too conservative or not
  conservative enough against real usage data — replace `ConservativeBudgetEstimator` with a
  smarter `IBudgetEstimator` implementation, not a change to admission logic; every caller depends
  only on the interface, by design.
- The first time orphaned `TaskAdmissionReservation` rows from crashed processes become numerous
  enough to matter — build the reconciliation sweep explicitly deferred this week (Confirmation
  #5).
- The first time Task 13's deferred `AgentBase.InvokeToolAsync` budget-check integration test is
  written — confirms the `MaxToolCallsPerTask` end-to-end path (RejectionEvent write + Failed
  propagation) actually behaves as designed, not just the isolated counter and the
  `MaxAgentsPerTask` half.
- The first time a second post-hoc-eligible consumer of `IEvalRunQueue` needs queue-depth
  semantics stricter than the accepted check-then-increment race (Task 10) — revisit whether the
  admission-transaction's CAS-based approach (Confirmation #4) should replace it.
- The first time an operator reports a tenant's rate limit change not taking effect — implement
  partition-key versioning (`{tenantId}:{limitsVersion}`, `SetTenantLimitsCommand` bumping the
  version on every update) to close Bug #2 (bucket immutability after a limits change), the
  implementation note directly above.

## ADR-016: Delivery and Operational Safety

**Status:** Accepted

### Investigation — what already existed vs. what's net-new
Before this week, there was no `.github/` directory at all — no CI, no Dependabot. Health checking
was a single unconditional-200 `/health` endpoint with no DB dependency of any kind. Startup
configuration validation existed for exactly one value (`Anthropic:ApiKey`, thrown from deep inside
`AddInfrastructure`); `ConnectionStrings:DefaultConnection` had none. Every one of the 12 existing
EF Core migrations was purely additive with an already-correct, EF-generated `Down()` — no migration
reversibility policy existed because nothing had yet violated one. `IReadinessChecker`,
`DatabaseReadinessChecker`, `RequiredConfigurationValidator`, and the entire `.github/` tree are
net-new this week.

### Confirmation #1 — CI triggers on push to `main`, not `pull_request`
This project has merged locally and pushed directly to `main` for 11 prior weeks — no PR-based
workflow exists or is being introduced now. A workflow gated only on `pull_request` events would
simply never run in practice. `ci.yml` triggers on `push: branches: [main]`, functioning as a
post-hoc safety net and portfolio signal (a green check on GitHub) rather than a pre-merge gate —
the actual merge gate remains what it has always been: a clean local `dotnet build`/`dotnet test`
before pushing. `workflow_dispatch` is also enabled, solely so a scratch branch can trigger a manual
run (used to prove the gate actually catches failures — see the Tests section of this week's plan)
without altering the `push` trigger itself.

### Confirmation #2 — Liveness/readiness split, and the hosted-service readiness finding
`/health/live` has zero dependencies and always returns `200` — a process that's up but can't reach
its database should still be reported "alive" (it isn't crash-looping) while `/health/ready` alone
governs whether Railway routes traffic to it. `/health/ready` is backed by `IReadinessChecker`
(`DatabaseReadinessChecker` in Infrastructure): `200` only if `Database.CanConnectAsync()` succeeds
**and** `Database.GetPendingMigrationsAsync()` returns empty, else `503` with a `reason`.
`railway.json`'s `healthcheckPath` was moved from `/health` to `/health/ready` so Railway's
redeploy health-gating actually reflects real DB/schema state, not a static always-200 stub.

**Hosted-service readiness — investigated, explicitly ruled out.** Both existing `BackgroundService`s
(`CostRollupBackgroundService`, `EvalRunBackgroundWorker`) were read in full. Neither has any
meaningful asynchronous initialization phase: `CostRollupBackgroundService.ExecuteAsync`'s very
first action inside its loop *is* the actual rollup work (no setup step precedes it);
`EvalRunBackgroundWorker.ExecuteAsync`'s very first statement is `await _queue.DequeueAsync(...)` —
it's immediately ready to process the moment `ExecuteAsync` starts. Neither service has a genuine
"not ready yet" state for `/health/ready` to gate on. No readiness-gating mechanism was built for
this — inventing one for a problem that doesn't exist would be exactly the kind of premature
abstraction `DESIGN_PRINCIPLES.md` argues against. If a future hosted service *does* need real
async init (e.g. warming a large in-memory index before it can safely process anything), this is the
trigger to revisit — see below.

**Startup-time DB outage — live but not ready, distinguished from a genuine migration failure
(found and fixed in Task 3, commit `4596e07`).** Before this fix, `Program.cs`'s auto-migrate-at-
startup called `dbContext.Database.MigrateAsync()` unconditionally before `app.Run()`. If the
database was unreachable at container boot, `MigrateAsync()` threw before Kestrel ever bound a port,
which meant the outer fail-fast catch (Confirmation #8, below) fired and crashed the whole process —
so `/health/live` was, in practice, **not reachable** during a startup-time DB outage at all,
directly contradicting this confirmation's own stated liveness goal ("`/health/live` returns 200 if
the process is running, full stop"). A process that can't yet reach its database during a transient
outage would fail to report "alive" purely because of a startup-ordering accident, not because the
process itself was unhealthy.

Fixed by checking `Database.CanConnectAsync()` first — reusing the exact same signal
`DatabaseReadinessChecker` itself uses — before attempting migration. If the database is reachable,
migration and seeding proceed exactly as before. If it is genuinely unreachable, migration/seeding
is skipped (`Log.Warning`, not thrown) and the app continues starting normally: Kestrel binds,
`/health/live` is unconditionally `200`, and `/health/ready` correctly reports `503 "database
unreachable"` until the database recovers — live, but not ready, exactly matching the
liveness/readiness contract this confirmation establishes for every other case.

This fix is deliberately narrow. If the database **is** reachable but `MigrateAsync()` itself then
throws — a genuine migration bug: bad SQL, a broken `Down()`/`Up()` — that exception is explicitly
NOT caught by this new logic. It propagates unchanged to Confirmation #8's outer fail-fast catch and
crashes the process loudly (`Log.Fatal`, `Environment.ExitCode = 1`), exactly as any other startup
failure. A prior fix attempt wrapped the entire migrate-plus-seed block in a blanket
`catch (Exception)` and was rejected during implementation, then reverted before landing: it would
have silently downgraded a genuine migration bug to the same Warning-and-continue path used for a
transient connectivity issue, masking exactly the class of failure Confirmation #8's fail-fast
philosophy exists to surface loudly. A transient connectivity problem and a genuine migration defect
must never be treated as the same event, and this fix's narrowness is what keeps them distinct.
`Program.cs`'s comment at this call site points back here.

**Why `/health/ready`'s migration check is not tautological despite `Program.cs` always
auto-migrating at startup.** Since `Program.cs` unconditionally calls `dbContext.Database
.MigrateAsync()` before `app.Run()` (when the database is reachable — see above), a container
that's actually serving traffic has, by definition, already migrated itself — so immediately after
startup, the pending-migrations check will always report zero pending. Its real value is a live
drift detector, not a startup gate: if an operator manually reverts the database schema while the
app container keeps running (exactly the scenario `RUNBOOK.md`'s migration-rollback guidance can
require), `/health/ready` correctly flips to `503` on the very next poll, surfacing the code/schema
mismatch instead of silently continuing to serve requests against a schema the running binary no
longer matches.

### Confirmation #3 — Migration validation proves schema convergence, not just test outcomes
The `migration-validation` CI job runs two Postgres services in parallel (`postgres-fresh`,
`postgres-upgrade`, different host ports) so both final schemas can be diffed directly. Scenario
(a): `dotnet ef database update` applied to a genuinely empty database. Scenario (b): migrate to the
migration immediately prior to latest, then apply latest on top — simulating a real production
upgrade — followed by the **full test suite run a second time** against that upgraded database.
Finally, `pg_dump --schema-only` both resulting databases and `diff` them: a non-empty diff fails
the job even if every test on both paths passed, since fresh-install test success alone cannot prove
an upgrade is safe (a constraint that's fine on an empty table but conflicts with existing data is
exactly the class of bug this catches that test-outcome checking alone would miss).

### Confirmation #4 — Container build validates the exact production artifact
The `container-smoke-test` job runs `docker build -f Dockerfile .` from the repo root with no build
args and no CI-only Dockerfile — the identical artifact `railway.json` (`"builder": "DOCKERFILE",
"dockerfilePath": "Dockerfile"`, no `startCommand` override) has Railway build. The built image is
then actually run (`docker run --network host`, real Postgres service, no pre-migration) and polled
against `/health/ready` until healthy or a 60-second timeout — proving the container migrates and
becomes ready unattended, mirroring a real fresh Railway deploy rather than a parallel, potentially
divergent build path.

### Confirmation #5 — Security scanning scope
`dotnet list package --vulnerable --include-transitive` (its own exit code is always `0` regardless
of findings — CI greps the text output for `High`/`Critical` markers and fails explicitly), a Trivy
container-image scan (`HIGH,CRITICAL`, `ignore-unfixed: true` — scanning any real base image
routinely surfaces OS-level CVEs with no published fix yet; failing on those would make the gate
permanently red for reasons outside this project's control), a manual verification checklist item
for GitHub secret scanning/push protection (a repo Settings toggle, not expressible in workflow
YAML — public repos have secret scanning on by default, verified via the repo's Settings →
Code security page, not a custom check), and `.github/dependabot.yml`. Dependabot is the one
deliberate, expected exception to this project's otherwise PR-less workflow — its update PRs are
reviewed and merged locally like everything else, not auto-merged.

### Confirmation #6 — Branch protection scoped to not conflict with the existing workflow
No "PR required before merge" setting — that would break the established local-merge-and-push
habit this project has used for 11 prior weeks. `main`'s branch protection is: `required_linear_history:
true`, `allow_force_pushes: false`, `allow_deletions: false`, `enforce_admins: true` (deliberately —
on a solo-owner repo, `enforce_admins: false` would exempt the only committer from all three of
those protections, making them theater). CI here is a second, independent, automated confirmation
that runs *after* the existing local engineering gate (a clean worktree build/test before merge) —
not a replacement for that discipline.

**A real, permanent GitHub limitation discovered while landing this exact change: `required_status_checks`
cannot coexist with a direct-push-to-`main` workflow.** The original plan and this ADR's first draft
specified requiring all four CI job status checks (`build-and-test`, `migration-validation`,
`container-smoke-test`, `security-scan`) on `main`'s HEAD. Applying that setting and then attempting
the very first real push to `main` under it (merging this week's own branch) failed immediately:
`GH006: Protected branch update failed for refs/heads/main — 4 of 4 required status checks are
expected`. `required_status_checks` does not distinguish "gate a PR merge" from "gate any push" — it
blocks **any** ref update to the protected branch (a direct push included) unless that exact commit
SHA already has recorded passing checks. Since a check can only be recorded by a workflow run that
the push itself triggers, requiring it for a direct push creates an unbreakable deadlock: the commit
cannot land until it has passing checks, and it cannot get checks run against it until it lands (or
at least exists on the remote to trigger a workflow). This is not a misconfigured value — it is a
fundamental mismatch between what the GitHub feature was designed for (gating a PR's merge, where
the check runs against the PR branch *before* it becomes part of the target branch) and how this
repo's main development flow actually works (no PRs, ever, for direct engineer pushes).

Fixed by removing `required_status_checks` entirely from `main`'s protection, keeping every other
setting (`enforce_admins`, `required_linear_history`, `allow_force_pushes: false`,
`allow_deletions: false`) — those are structural rules with no status-check dependency, so they
protect the branch with no risk of the same deadlock. This actually *restores* this confirmation's
original intent rather than merely patching around a bug: the stated goal from the start was "CI as
a visible signal and safety net, not a hard gate," precisely because this project has no PR-based
merge flow — the originally-applied config accidentally implemented a hard gate, contradicting that
intent. Verified by retrying the same push immediately after the fix: it succeeded, and the CI
workflow still triggered and reported all four job check-runs as `success` against the landed commit
(`gh api repos/.../commits/<sha>/check-runs`) — CI still runs and shows status on every push, it
simply no longer blocks the ref update.

**Accept explicitly, not implicitly: there is no GitHub branch-protection configuration that gives
both "hard-gate on CI" and "never require a PR."** This is a real, permanent tradeoff of the
platform, not a temporary gap to close later. One consequence worth stating plainly:
Dependabot/security-update PRs — the one place PRs legitimately enter this otherwise PR-less
workflow (Confirmation #5) — are **not** automatically blocked from merging by a failing check
either, since the same `required_status_checks` removal applies to them too. Merging a Dependabot PR
still requires manually confirming its CI run is green first; nothing in branch protection enforces
that for you.

### Confirmation #7 — Migration reversibility policy
Every migration's `Down()` must either perform real `migrationBuilder` work (purely additive,
structurally reversible — every one of the 12 existing migrations already qualifies) or throw
`NotSupportedException` with a documented reason (irreversible — a data transformation or
destructive change). Enforced going forward by `MigrationReversibilityTests`
(`tests/OrchestAI.Tests/Architecture/`), a static-analysis test over migration source files that
fails the build the moment a future migration ships an empty or thoughtless `Down()` — the same
"enforced by a test, not just by review" pattern `LayeringTests` already established for
architectural layering. **Production rollback does not mean executing migration `Down()`s against a
live database** — see `RUNBOOK.md`: rollback means redeploying the previous application version
against a schema that remains backward-compatible with it, following the same
nullable-→-backfill→-non-null multi-step pattern Week 10 already established for any non-purely-
additive change.

### Confirmation #8 — Fail-fast configuration validation
`RequiredConfigurationValidator.Validate` (Infrastructure, called as the first line of
`AddInfrastructure`) checks `ConnectionStrings:DefaultConnection` and `Anthropic:ApiKey` together,
throwing one `InvalidOperationException` listing every missing/blank key at once — replacing the old
ad-hoc single-key check (`Anthropic:ApiKey` only; `ConnectionStrings:DefaultConnection` had zero
validation and would only ever fail lazily, inside the auto-migrate call, with whatever exception
Npgsql happened to throw). **`Admin:BootstrapSecret` is deliberately excluded from required
validation** — `RequireAdminSecretFilter` already fails gracefully per-request (`503`) when it's
unset, a correct, already-shipped posture for an operator-only admin-bootstrap surface; a fresh
Railway deploy that hasn't configured admin bootstrap yet should still serve normal tenant traffic,
not refuse to start entirely.

A real bug was found and fixed alongside this: `Program.cs`'s top-level `catch (Exception ex)` never
set a non-zero exit code, so a genuine startup failure (bad config, unreachable DB) logged Fatal and
then exited `0` — which would defeat Railway's `restartPolicyType: ON_FAILURE` (a `0` exit reads as
an intentional, successful shutdown, not something to restart from). Fixed by explicitly setting
`Environment.ExitCode = 1` in that catch block. This interacts with a second, independently
discovered bug: EF Core design-time tooling (`dotnet ef migrations list/add`, `database update`)
invokes `Program.Main` via reflection *in the same process* and throws
`Microsoft.Extensions.Hosting.HostAbortedException` as its documented mechanism for capturing the
built host without running it — confirmed by actually running `dotnet ef migrations list` locally
during this week's investigation, not assumed. Adding the exit-code fix to the *unconditional* catch
would have poisoned `Environment.ExitCode` to `1` for every subsequent `dotnet ef` invocation in the
same process (the tool's own successful-completion path never resets that shared mutable value),
silently breaking any CI step that checks `dotnet ef`'s exit code — exactly the failure mode
`DESIGN_PRINCIPLES.md`'s "Empirical verification over plausible-sounding review" section exists to
catch. Fixed with the standard, documented pattern: `catch (Exception ex) when (ex is not
HostAbortedException)`.

### Confirmation #9 — Artifact retention on CI failure
Every job redirects its own diagnostic command output to files (test `.trx`, migration command
output, container logs, health-check polling responses, vulnerability-scan output) and uploads them
via `actions/upload-artifact@v4` with `if: always()` — not conditioned on failure, since a clean
run's artifacts are still useful as an audit trail and this avoids branching upload logic by
job outcome. A bare "container smoke test failed" with nothing to inspect would force a re-run just
to get diagnostic information; this optimizes for investigation speed over minimal storage use (14-
day retention, GitHub Actions' artifact defaults otherwise apply).

### Implementation notes
**A real, empirically-confirmed bug was found purely by running a command, not by reading code.**
See Confirmation #8 above — the `HostAbortedException`/exit-code interaction would not have been
caught by review alone; it was only found by actually running `dotnet ef migrations list` during
this week's investigation phase and observing the unexpected Fatal log line, then reasoning through
why the naive exit-code fix would have made it worse, not better.

**A real bug in the liveness/readiness split itself was found purely by re-reading the startup path
against this ADR's own stated goal, not by a new failing test.** See Confirmation #2's
startup-time-DB-outage material above — the auto-migrate-at-startup call unconditionally throwing
into the fail-fast catch meant `/health/live` was unreachable during exactly the outage scenario it
exists to remain reachable through. Fixed in Task 3, commit `4596e07`, by checking
`Database.CanConnectAsync()` before attempting migration, while deliberately leaving a genuine
`MigrateAsync()` failure on the fail-fast path untouched.

### Trigger for revisiting
- The first time a hosted service is added that genuinely needs asynchronous initialization before
  it can safely process work (unlike `CostRollupBackgroundService`/`EvalRunBackgroundWorker` today)
  — build a real readiness-gating mechanism for it at that point, not before.
- The first time CI wall-clock time becomes a bottleneck on this project's own iteration speed —
  revisit whether `migration-validation`'s duplicate full-test-suite run (once in `build-and-test`,
  once against the upgrade-path DB) is worth splitting or caching further.
- The first time a second application (not just Dependabot) needs to open a PR against this
  otherwise PR-less workflow — revisit whether branch protection's `required_linear_history`
  assumption still holds.
