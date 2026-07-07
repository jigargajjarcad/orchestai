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
