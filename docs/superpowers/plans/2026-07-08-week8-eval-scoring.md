# Week 8: Evaluation & Scoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a pluggable evaluation/regression-detection layer — suites of test cases run against a real agent type, scored by rule-based or LLM-judge scorers, compared against an explicit baseline run, with eval cost segregated from production cost dashboards.

**Architecture:** `RunEvalSuiteCommand` creates an `EvalRun` row and enqueues it; a new `EvalRunBackgroundWorker` (same shape as Week 7's `CostRollupBackgroundService`) dequeues it, and for each `EvalCase` spins up a fresh `OrchestrationTask` owned by a dedicated `EvalSystemUser`, invokes the suite's target agent directly via `IAgentFactory` (no `OrchestratorAgent` planning — eval targets one agent type in isolation), scores the result with the case's declared scorer, and persists an `EvalResult`. `EvalRunId` is threaded through `IAgent.ExecuteAsync` → `AgentExecution` → `CostLedger`, and a new `CostLedger.Source` discriminator (`Production`/`Eval`) keeps judge-call and eval-invocation cost out of `GetDailyAggregatesAsync` (the single choke point feeding both the rollup background job and the live "today" dashboard).

**Tech Stack:** C# .NET 8, EF Core 8 / PostgreSQL, MediatR (CQRS), xUnit + Moq + FluentAssertions, EF Core InMemory (new, test-only), React (existing single-file page pattern).

## Global Constraints

- Controllers handle HTTP only — all logic in MediatR handlers.
- CQRS for everything — commands mutate, queries read.
- `IAgent`/`IMcpTool`-style interfaces, never concrete dependencies, for anything pluggable (scorers).
- Always async/await with `CancellationToken` throughout.
- Structured logging on all agent/eval operations, mirroring existing `AgentBase`/`CostRollupBackgroundService` logging.
- Never `.Result` or `.Wait()`.
- `EvalResult` rows are immutable after creation — no update method on the entity (mirrors `CostLedger`'s write-once cost-immutability guarantee).
- No frontend authoring UI for eval cases this week — seed via the new API endpoints directly (Postman/curl/script), not a form.
- No dataset versioning/branching, no statistical significance testing, no retroactive/post-hoc scoring — threshold comparison and live-execution only.

## Blocking confirmations — resolved before any task below

**1. Is `TraceId` a stable, independently-referenceable PK?** No. `OrchestrationTask.TraceId` is an OTel-shaped hex *string* correlation column (`DECISIONS.md` ADR-011 Decision 2), not a primary key — `OrchestrationTask.Id` (Guid) is the PK, and `TraceId` has no uniqueness constraint of its own in `OrchestrationTaskConfiguration`. The stable, independently-referenceable PK for a single agent invocation is `AgentExecution.Id`, which ADR-011 Decision 3 already anticipated: *"`EvaluationResults` (Week 8) will FK to `AgentExecution.Id` (already a stable PK)."* `EvalResult` therefore gets an `AgentExecutionId` FK, not a literal `trace_id` column. This still satisfies "must work for both live-execution and future post-hoc scoring" (Week 9) — post-hoc scoring will score an existing production `AgentExecution` row through the exact same FK.

**2. Does `CostLedger` have a cost-source discriminator?** No — confirmed by reading `CostLedger.cs` and `CostLedgerConfiguration.cs`; the entity has no `Source` column today. Task 3 below adds `CostLedger.Source` (`CostSource` enum: `Production`/`Eval`, `NOT NULL DEFAULT 'Production'`) plus `CostLedger.EvalRunId` (nullable FK) via migration.

**3. How does `Source=Eval` get set at write time?** `IAgent.ExecuteAsync` gains a trailing optional `Guid? evalRunId = null` parameter (backward-compatible — existing callers in `StartOrchestrationHandler`/`ResumeOrchestrationTaskHandler` are unaffected). `AgentBase.ExecuteAsync` threads it into `AgentExecution.Create(...)` (new nullable `AgentExecution.EvalRunId` column — the trace/execution record itself, not just the cost row, per the spec's explicit requirement) and `FinalizeSuccessAsync` reads `execution.EvalRunId` to tag the `CostLedger` row's `Source`/`EvalRunId`. The eval background worker is the only caller that ever passes a non-null `evalRunId`. LLM-judge calls (which aren't `AgentExecution`s at all) write their own `CostLedger` row directly through `ICostLedgerRepository`, tagged `Source=Eval` and stamped with the current `EvalRunId`, reusing the per-case `OrchestrationTaskId` already in scope (required because `CostLedger.OrchestrationTaskId` is `NOT NULL`).

**Additional gap found during investigation (not in the original spec, but blocking for the LLM judge scorer):** nothing in the LLM call pipeline supports pinning temperature — `AgentConversation` has no `Temperature` field and neither `AnthropicProvider` nor the OpenAI-compatible mapper ever sets one. Confirmed via SDK metadata inspection that both `Anthropic.SDK.Messaging.MessageParameters` and `OpenAI.Chat.ChatCompletionOptions` expose a settable `Temperature` property that is simply never wired up today. Task 2 below adds this end-to-end since ADR-012's "temperature pinned to 0" claim would otherwise be undocumented fiction.

**Design decision on `IEvalScorer`'s signature:** the spec's literal `ScoreAsync(EvalCase, actualOutput) -> EvalScoreResult` cannot satisfy "judge calls must flow through the existing cost-tracking pipeline, tagged with `Source=Eval`" — the scorer needs an `OrchestrationTaskId` to write a legal `CostLedger` row and an `EvalRunId` to tag it. `IEvalScorer.ScoreAsync` therefore takes a third `EvalScoringContext` parameter carrying exactly those two IDs. `RuleBasedScorer` ignores it. This is documented as a resolved decision in ADR-012, not a silent deviation.

**Eval invocation owner:** every `OrchestrationTask` created for an eval-case invocation is owned by a dedicated seeded `EvalSystemUser` (mirrors the existing `DatabaseSeeder.DevUserId` pattern). This is a deliberate, minimal choice: it satisfies `OrchestrationTask.UserId`'s `NOT NULL` FK without inventing new schema, and it means eval-spawned tasks never pollute a real user's "recent tasks" list / observability task picker (`GetRecentByUserIdAsync` is scoped by `UserId`) — for free, with no new flag or query change needed.

---

## Task 1: `CostSource`, `EvalRunStatus`, `EvalScorerType` enums

**Files:**
- Create: `src/OrchestAI.Domain/Enums/CostSource.cs`
- Create: `src/OrchestAI.Domain/Enums/EvalRunStatus.cs`
- Create: `src/OrchestAI.Domain/Enums/EvalScorerType.cs`

**Interfaces:**
- Produces: `CostSource { Production, Eval }`, `EvalRunStatus { Pending, Running, Completed, Failed }`, `EvalScorerType { RuleBased, LlmJudge }` — used by every later task in this plan.

- [ ] **Step 1: Create the enums**

```csharp
// src/OrchestAI.Domain/Enums/CostSource.cs
namespace OrchestAI.Domain.Enums;

public enum CostSource
{
    Production,
    Eval
}
```

```csharp
// src/OrchestAI.Domain/Enums/EvalRunStatus.cs
namespace OrchestAI.Domain.Enums;

public enum EvalRunStatus
{
    Pending,
    Running,
    Completed,
    Failed
}
```

```csharp
// src/OrchestAI.Domain/Enums/EvalScorerType.cs
namespace OrchestAI.Domain.Enums;

public enum EvalScorerType
{
    RuleBased,
    LlmJudge
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/OrchestAI.Domain/OrchestAI.Domain.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/OrchestAI.Domain/Enums/CostSource.cs src/OrchestAI.Domain/Enums/EvalRunStatus.cs src/OrchestAI.Domain/Enums/EvalScorerType.cs
git commit -m "feat: add CostSource, EvalRunStatus, EvalScorerType enums"
```

---

## Task 2: Thread `Temperature` through the LLM provider pipeline

**Files:**
- Modify: `src/OrchestAI.Domain/Models/AgentConversation.cs`
- Modify: `src/OrchestAI.Infrastructure/Providers/AnthropicProvider.cs`
- Modify: `src/OrchestAI.Infrastructure/Providers/OpenAiChatMapper.cs`
- Test: `tests/OrchestAI.Tests/Infrastructure/AnthropicProviderTemperatureTests.cs`

**Interfaces:**
- Produces: `AgentConversation.Temperature` (`double?`, default `null` — existing callers unaffected). Consumed by Task 11 (`LlmJudgeScorer`), which is the only caller that ever sets it.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Infrastructure/AnthropicProviderTemperatureTests.cs
using FluentAssertions;
using Moq;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Providers;
using Anthropic.SDK.Messaging;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AnthropicProviderTemperatureTests
{
    [Fact]
    public async Task SendAsync_ConversationHasTemperature_ForwardsItToTheClient()
    {
        var clientMock = new Mock<IAnthropicClientWrapper>();
        MessageParameters? captured = null;
        clientMock
            .Setup(c => c.CreateMessageAsync(It.IsAny<MessageParameters>(), It.IsAny<CancellationToken>()))
            .Callback<MessageParameters, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new MessageResponse
            {
                Content = [new TextContent { Text = "0.42" }],
                StopReason = "end_turn",
                Usage = new Usage { InputTokens = 1, OutputTokens = 1 }
            });

        var provider = new AnthropicProvider(clientMock.Object);
        var conversation = new AgentConversation(
            "system", Messages: [new ConversationMessage("user", "hi")], Tools: [],
            Model: "claude-haiku-4-5-20251001", MaxTokens: 10, Temperature: 0.0);

        await provider.SendAsync(conversation, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Temperature.Should().Be(0.0m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter AnthropicProviderTemperatureTests`
Expected: FAIL — `AgentConversation` has no `Temperature` parameter, compile error.

- [ ] **Step 3: Add `Temperature` to `AgentConversation`**

```csharp
// src/OrchestAI.Domain/Models/AgentConversation.cs — change the record header to:
public sealed record AgentConversation(
    string SystemPrompt,
    IReadOnlyList<ConversationMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    string Model,
    int MaxTokens,
    double? Temperature = null)
```

(Leave `AppendToolResults` and the rest of the file unchanged.)

- [ ] **Step 4: Forward it in `AnthropicProvider`**

```csharp
// src/OrchestAI.Infrastructure/Providers/AnthropicProvider.cs — in SendAsync, change the
// MessageParameters initializer to:
        var response = await _client.CreateMessageAsync(
            new MessageParameters
            {
                Model = conversation.Model,
                MaxTokens = conversation.MaxTokens,
                System = [new SystemMessage(conversation.SystemPrompt)],
                Messages = conversation.Messages.Select(BuildMessage).ToList(),
                Stream = false,
                Tools = BuildTools(conversation.Tools),
                Temperature = (decimal?)conversation.Temperature
            }, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 5: Forward it in `OpenAiChatMapper`**

```csharp
// src/OrchestAI.Infrastructure/Providers/OpenAiChatMapper.cs — in BuildRequest, change:
        var options = new ChatCompletionOptions { MaxOutputTokenCount = conversation.MaxTokens };
        if (conversation.Temperature.HasValue)
            options.Temperature = (float)conversation.Temperature.Value;
        foreach (var tool in conversation.Tools)
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter AnthropicProviderTemperatureTests`
Expected: PASS

- [ ] **Step 7: Run the full suite to confirm no regressions**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: all tests pass (existing `AgentBaseProviderTests`/provider tests construct `AgentConversation` positionally/by name without `Temperature` — the new parameter is optional and defaults to `null`, so nothing else should need to change).

- [ ] **Step 8: Commit**

```bash
git add src/OrchestAI.Domain/Models/AgentConversation.cs src/OrchestAI.Infrastructure/Providers/AnthropicProvider.cs src/OrchestAI.Infrastructure/Providers/OpenAiChatMapper.cs tests/OrchestAI.Tests/Infrastructure/AnthropicProviderTemperatureTests.cs
git commit -m "feat: support pinning LLM call temperature, needed for reproducible judge scoring"
```

---

## Task 3: Thread `EvalRunId` through `IAgent` → `AgentBase` → `CostLedger`

**Files:**
- Modify: `src/OrchestAI.Domain/Interfaces/IAgent.cs`
- Modify: `src/OrchestAI.Domain/Entities/AgentExecution.cs`
- Modify: `src/OrchestAI.Domain/Entities/CostLedger.cs`
- Modify: `src/OrchestAI.Infrastructure/Agents/Base/AgentBase.cs`
- Test: `tests/OrchestAI.Tests/Infrastructure/AgentBaseProviderTests.cs` (add one test)

**Interfaces:**
- Consumes: `CostSource` from Task 1.
- Produces: `IAgent.ExecuteAsync(..., Guid? evalRunId = null)`, `AgentExecution.EvalRunId` (nullable Guid), `CostLedger.Source`/`CostLedger.EvalRunId` — consumed by Task 6 (EF configs), Task 8 (repository filter), Task 19 (background worker).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Infrastructure/AgentBaseProviderTests.cs — add this test to the
// existing class (it already has _providerMock/_toolRegistryMock/_costRepoMock wired up
// in the constructor, and BuildAgent() below):

    [Fact]
    public async Task ExecuteAsync_EvalRunIdPassed_TagsExecutionAndCostLedgerAsEval()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 10, 5));

        AgentExecution? capturedExecution = null;
        _execRepoMock
            .Setup(r => r.AddAsync(It.IsAny<AgentExecution>(), It.IsAny<CancellationToken>()))
            .Callback<AgentExecution, CancellationToken>((e, _) => capturedExecution = e)
            .Returns(Task.CompletedTask);

        CostLedger? capturedLedger = null;
        _costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((l, _) => capturedLedger = l)
            .Returns(Task.CompletedTask);

        var evalRunId = Guid.NewGuid();
        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None, evalRunId: evalRunId);

        capturedExecution!.EvalRunId.Should().Be(evalRunId);
        capturedLedger!.Source.Should().Be(CostSource.Eval);
        capturedLedger.EvalRunId.Should().Be(evalRunId);
    }

    [Fact]
    public async Task ExecuteAsync_NoEvalRunId_TagsCostLedgerAsProduction()
    {
        _toolRegistryMock.Setup(r => r.GetTools(It.IsAny<IReadOnlyList<string>>())).Returns([]);
        _providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", "Done", [], 10, 5));

        CostLedger? capturedLedger = null;
        _costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((l, _) => capturedLedger = l)
            .Returns(Task.CompletedTask);

        var agent = BuildAgent();
        await agent.ExecuteAsync(TaskId, UserId, "Do something", CancellationToken.None);

        capturedLedger!.Source.Should().Be(CostSource.Production);
        capturedLedger.EvalRunId.Should().BeNull();
    }
```

Add `using OrchestAI.Domain.Enums;` to the test file's usings if not already present (it already imports `OrchestAI.Domain.Enums` for `AgentType`, so no change needed there).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter AgentBaseProviderTests`
Expected: FAIL — compile error, `AgentExecution.EvalRunId`/`CostLedger.Source` don't exist yet, `ExecuteAsync` has no `evalRunId` parameter.

- [ ] **Step 3: Extend `IAgent`**

```csharp
// src/OrchestAI.Domain/Interfaces/IAgent.cs
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IAgent
{
    Task<AgentExecutionResult> ExecuteAsync(
        Guid orchestrationTaskId,
        Guid userId,
        string userPrompt,
        CancellationToken cancellationToken = default,
        string? parentSpanId = null,
        Guid? evalRunId = null);
}
```

- [ ] **Step 4: Extend `AgentExecution`**

```csharp
// src/OrchestAI.Domain/Entities/AgentExecution.cs — add the property:
    public Guid? EvalRunId { get; private set; }

// and change Create's signature + body to:
    public static AgentExecution Create(
        Guid taskId, AgentType agentType, string inputPrompt, string? parentSpanId = null,
        Guid? evalRunId = null)
    {
        return new AgentExecution
        {
            Id = Guid.NewGuid(),
            OrchestrationTaskId = taskId,
            AgentType = agentType,
            Status = ExecutionStatus.Pending,
            InputPrompt = inputPrompt,
            SpanId = TraceIdentifiers.NewSpanId(),
            ParentSpanId = parentSpanId,
            EvalRunId = evalRunId,
            InputTokens = 0,
            OutputTokens = 0,
            CostUsd = 0,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
```

- [ ] **Step 5: Extend `CostLedger`**

```csharp
// src/OrchestAI.Domain/Entities/CostLedger.cs
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class CostLedger
{
    private CostLedger() { }

    public Guid Id { get; private set; }
    public Guid OrchestrationTaskId { get; private set; }
    public Guid? AgentExecutionId { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public CostSource Source { get; private set; }
    public Guid? EvalRunId { get; private set; }
    public DateTimeOffset RecordedAt { get; private set; }

    public OrchestrationTask OrchestrationTask { get; private set; } = null!;
    public AgentExecution? AgentExecution { get; private set; }

    public static CostLedger Create(
        Guid orchestrationTaskId,
        string model,
        int inputTokens,
        int outputTokens,
        decimal costUsd,
        Guid? agentExecutionId = null,
        CostSource source = CostSource.Production,
        Guid? evalRunId = null)
    {
        return new CostLedger
        {
            Id = Guid.NewGuid(),
            OrchestrationTaskId = orchestrationTaskId,
            AgentExecutionId = agentExecutionId,
            Model = model,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd,
            Source = source,
            EvalRunId = evalRunId,
            RecordedAt = DateTimeOffset.UtcNow
        };
    }
}
```

- [ ] **Step 6: Thread `evalRunId` through `AgentBase`**

```csharp
// src/OrchestAI.Infrastructure/Agents/Base/AgentBase.cs — change ExecuteAsync's signature to:
    public async Task<AgentExecutionResult> ExecuteAsync(
        Guid orchestrationTaskId,
        Guid userId,
        string userPrompt,
        CancellationToken cancellationToken = default,
        string? parentSpanId = null,
        Guid? evalRunId = null)
    {
        var safePrompt = RedactIfEnabled(userPrompt);

        var execution = await SetupExecutionAsync(
            orchestrationTaskId, safePrompt, cancellationToken, parentSpanId, evalRunId)
            .ConfigureAwait(false);
```

(The rest of the method body is unchanged — `execution` already carries `EvalRunId` from here on.)

```csharp
// SetupExecutionAsync — add the parameter and pass it through:
    protected async Task<AgentExecution> SetupExecutionAsync(
        Guid orchestrationTaskId,
        string userPrompt,
        CancellationToken cancellationToken,
        string? parentSpanId = null,
        Guid? evalRunId = null)
    {
        var execution = AgentExecution.Create(orchestrationTaskId, AgentType, userPrompt, parentSpanId, evalRunId);
        await _agentExecutionRepository.AddAsync(execution, cancellationToken).ConfigureAwait(false);
        // ... unchanged from here
```

```csharp
// FinalizeSuccessAsync — tag the CostLedger row from the execution's own EvalRunId:
        var ledger = CostLedger.Create(
            execution.OrchestrationTaskId,
            _agentOptions.Value.Models[AgentType.ToString()],
            inputTokens, outputTokens, costUsd,
            execution.Id,
            source: execution.EvalRunId is null ? CostSource.Production : CostSource.Eval,
            evalRunId: execution.EvalRunId);
        await _costLedgerRepository.AddAsync(ledger, cancellationToken).ConfigureAwait(false);
```

Add `using OrchestAI.Domain.Enums;` to `AgentBase.cs` if not already present (it already imports `OrchestAI.Domain.Enums` for `AgentType`/`ExecutionErrorCategory`, so no change needed).

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter AgentBaseProviderTests`
Expected: PASS, all tests in the class including the two new ones.

- [ ] **Step 8: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: all pass. `StartOrchestrationHandler`/`ResumeOrchestrationTaskHandler` call `agent.ExecuteAsync(taskId, userId, prompt, cancellationToken, parentSpanId)` — four positional args plus one named/positional optional — unaffected by the new trailing optional parameter.

- [ ] **Step 9: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IAgent.cs src/OrchestAI.Domain/Entities/AgentExecution.cs src/OrchestAI.Domain/Entities/CostLedger.cs src/OrchestAI.Infrastructure/Agents/Base/AgentBase.cs tests/OrchestAI.Tests/Infrastructure/AgentBaseProviderTests.cs
git commit -m "feat: thread EvalRunId through agent execution and cost ledger writes"
```

---

## Task 4: `EvalSuite` and `EvalCase` entities

**Files:**
- Create: `src/OrchestAI.Domain/Entities/EvalSuite.cs`
- Create: `src/OrchestAI.Domain/Entities/EvalCase.cs`
- Test: `tests/OrchestAI.Tests/Domain/EvalCaseTests.cs`

**Interfaces:**
- Consumes: `AgentType` (existing), `EvalScorerType` (Task 1).
- Produces: `EvalSuite.Create(name, description, targetAgentType)`, `EvalCase.Create(suiteId, inputPayloadJson, expectedCriteriaJson, scorerType, regressionThreshold, tags)` — consumed by Task 13 (repositories), Task 16/17 (commands), Task 19 (background worker).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Domain/EvalCaseTests.cs
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class EvalCaseTests
{
    [Fact]
    public void Create_ValidInputs_SetsAllFields()
    {
        var suiteId = Guid.NewGuid();

        var evalCase = EvalCase.Create(
            suiteId, "{\"prompt\":\"hi\"}", "{\"mode\":\"ExactMatch\",\"expected\":\"hi\"}",
            EvalScorerType.RuleBased, 0.05m, "smoke,regression");

        evalCase.SuiteId.Should().Be(suiteId);
        evalCase.InputPayload.Should().Be("{\"prompt\":\"hi\"}");
        evalCase.ScorerType.Should().Be(EvalScorerType.RuleBased);
        evalCase.RegressionThreshold.Should().Be(0.05m);
        evalCase.Tags.Should().Be("smoke,regression");
        evalCase.Id.Should().NotBe(Guid.Empty);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalCaseTests`
Expected: FAIL — `EvalCase` doesn't exist, compile error.

- [ ] **Step 3: Create `EvalSuite`**

```csharp
// src/OrchestAI.Domain/Entities/EvalSuite.cs
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class EvalSuite
{
    private EvalSuite() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public AgentType TargetAgentType { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<EvalCase> _cases = [];
    public IReadOnlyCollection<EvalCase> Cases => _cases.AsReadOnly();

    public static EvalSuite Create(string name, string description, AgentType targetAgentType)
    {
        return new EvalSuite
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            TargetAgentType = targetAgentType,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

- [ ] **Step 4: Create `EvalCase`**

```csharp
// src/OrchestAI.Domain/Entities/EvalCase.cs
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class EvalCase
{
    private EvalCase() { }

    public Guid Id { get; private set; }
    public Guid SuiteId { get; private set; }
    public string InputPayload { get; private set; } = string.Empty;
    public string ExpectedCriteria { get; private set; } = string.Empty;
    public EvalScorerType ScorerType { get; private set; }
    public decimal RegressionThreshold { get; private set; }
    public string Tags { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public EvalSuite Suite { get; private set; } = null!;

    public static EvalCase Create(
        Guid suiteId,
        string inputPayload,
        string expectedCriteria,
        EvalScorerType scorerType,
        decimal regressionThreshold,
        string tags = "")
    {
        return new EvalCase
        {
            Id = Guid.NewGuid(),
            SuiteId = suiteId,
            InputPayload = inputPayload,
            ExpectedCriteria = expectedCriteria,
            ScorerType = scorerType,
            RegressionThreshold = regressionThreshold,
            Tags = tags,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalCaseTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Domain/Entities/EvalSuite.cs src/OrchestAI.Domain/Entities/EvalCase.cs tests/OrchestAI.Tests/Domain/EvalCaseTests.cs
git commit -m "feat: add EvalSuite and EvalCase domain entities"
```

---

## Task 5: `EvalRun` and `EvalResult` entities

**Files:**
- Create: `src/OrchestAI.Domain/Entities/EvalRun.cs`
- Create: `src/OrchestAI.Domain/Entities/EvalResult.cs`
- Test: `tests/OrchestAI.Tests/Domain/EvalRunTests.cs`

**Interfaces:**
- Consumes: `EvalRunStatus`, `EvalScorerType` (Task 1).
- Produces: `EvalRun.Create(suiteId, subjectVersion, baselineRunId)`, `.MarkRunning()`, `.MarkCompleted()`, `.MarkFailed(error)`; `EvalResult.Create(evalRunId, evalCaseId, agentExecutionId, scorerType, scorerVersion, score, passed, scorerOutputJson)` — consumed by Task 14/15 (repositories), Task 18/19 (run command + worker), Task 21/22 (result/regression queries).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Domain/EvalRunTests.cs
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class EvalRunTests
{
    [Fact]
    public void Create_NoBaseline_StartsPendingWithNullBaseline()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", baselineRunId: null);

        run.Status.Should().Be(EvalRunStatus.Pending);
        run.BaselineRunId.Should().BeNull();
        run.SubjectVersion.Should().Be("commit-abc123");
    }

    [Fact]
    public void MarkRunning_ThenMarkCompleted_TransitionsStatusAndSetsCompletedAt()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", null);

        run.MarkRunning();
        run.Status.Should().Be(EvalRunStatus.Running);

        run.MarkCompleted();
        run.Status.Should().Be(EvalRunStatus.Completed);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void MarkFailed_SetsStatusAndErrorMessage()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", null);

        run.MarkFailed("suite has no cases");

        run.Status.Should().Be(EvalRunStatus.Failed);
        run.ErrorMessage.Should().Be("suite has no cases");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalRunTests`
Expected: FAIL — `EvalRun` doesn't exist.

- [ ] **Step 3: Create `EvalRun`**

```csharp
// src/OrchestAI.Domain/Entities/EvalRun.cs
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class EvalRun
{
    private EvalRun() { }

    public Guid Id { get; private set; }
    public Guid SuiteId { get; private set; }
    public DateTimeOffset TriggeredAt { get; private set; }
    public EvalRunStatus Status { get; private set; }
    public Guid? BaselineRunId { get; private set; }
    public string SubjectVersion { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public EvalSuite Suite { get; private set; } = null!;
    public EvalRun? BaselineRun { get; private set; }

    public static EvalRun Create(Guid suiteId, string subjectVersion, Guid? baselineRunId)
    {
        return new EvalRun
        {
            Id = Guid.NewGuid(),
            SuiteId = suiteId,
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = EvalRunStatus.Pending,
            BaselineRunId = baselineRunId,
            SubjectVersion = subjectVersion
        };
    }

    public void MarkRunning() => Status = EvalRunStatus.Running;

    public void MarkCompleted()
    {
        Status = EvalRunStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string error)
    {
        Status = EvalRunStatus.Failed;
        ErrorMessage = error;
        CompletedAt = DateTimeOffset.UtcNow;
    }
}
```

- [ ] **Step 4: Create `EvalResult`**

```csharp
// src/OrchestAI.Domain/Entities/EvalResult.cs
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

// Immutable after creation — a score is a point-in-time record of what the scorer
// concluded when the run executed, never re-derived from current scorer logic on read.
public sealed class EvalResult
{
    private EvalResult() { }

    public Guid Id { get; private set; }
    public Guid EvalRunId { get; private set; }
    public Guid EvalCaseId { get; private set; }
    public Guid? AgentExecutionId { get; private set; }
    public EvalScorerType ScorerType { get; private set; }
    public string ScorerVersion { get; private set; } = string.Empty;
    public decimal Score { get; private set; }
    public bool Passed { get; private set; }
    public string ScorerOutput { get; private set; } = string.Empty;
    public DateTimeOffset ScoredAt { get; private set; }

    public EvalRun Run { get; private set; } = null!;
    public EvalCase Case { get; private set; } = null!;

    public static EvalResult Create(
        Guid evalRunId,
        Guid evalCaseId,
        Guid? agentExecutionId,
        EvalScorerType scorerType,
        string scorerVersion,
        decimal score,
        bool passed,
        string scorerOutput)
    {
        return new EvalResult
        {
            Id = Guid.NewGuid(),
            EvalRunId = evalRunId,
            EvalCaseId = evalCaseId,
            AgentExecutionId = agentExecutionId,
            ScorerType = scorerType,
            ScorerVersion = scorerVersion,
            Score = score,
            Passed = passed,
            ScorerOutput = scorerOutput,
            ScoredAt = DateTimeOffset.UtcNow
        };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalRunTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Domain/Entities/EvalRun.cs src/OrchestAI.Domain/Entities/EvalResult.cs tests/OrchestAI.Tests/Domain/EvalRunTests.cs
git commit -m "feat: add EvalRun and EvalResult domain entities"
```

---

## Task 6: EF Core configurations for the eval model, updated `CostLedger`/`AgentExecution` configs, `AppDbContext` registration

**Files:**
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/EvalSuiteConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/EvalCaseConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/EvalRunConfiguration.cs`
- Create: `src/OrchestAI.Infrastructure/Data/Configurations/EvalResultConfiguration.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/Configurations/CostLedgerConfiguration.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/Configurations/AgentExecutionConfiguration.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/AppDbContext.cs`

**Interfaces:**
- Consumes: entities from Tasks 3–5.
- Produces: `AppDbContext.EvalSuites/EvalCases/EvalRuns/EvalResults` `DbSet`s — consumed by every repository task (13–15) and by Task 7's migration.

- [ ] **Step 1: `EvalSuiteConfiguration`**

```csharp
// src/OrchestAI.Infrastructure/Data/Configurations/EvalSuiteConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalSuiteConfiguration : IEntityTypeConfiguration<EvalSuite>
{
    public void Configure(EntityTypeBuilder<EvalSuite> builder)
    {
        builder.ToTable("EvalSuites");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(s => s.Description)
            .IsRequired();

        builder.Property(s => s.TargetAgentType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasMany(s => s.Cases)
            .WithOne(c => c.Suite)
            .HasForeignKey(c => c.SuiteId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.TargetAgentType);
    }
}
```

- [ ] **Step 2: `EvalCaseConfiguration`**

```csharp
// src/OrchestAI.Infrastructure/Data/Configurations/EvalCaseConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalCaseConfiguration : IEntityTypeConfiguration<EvalCase>
{
    public void Configure(EntityTypeBuilder<EvalCase> builder)
    {
        builder.ToTable("EvalCases");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.SuiteId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(c => c.InputPayload)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(c => c.ExpectedCriteria)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(c => c.ScorerType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(c => c.RegressionThreshold)
            .IsRequired()
            .HasColumnType("decimal(5,4)");

        builder.Property(c => c.Tags)
            .IsRequired()
            .HasMaxLength(500)
            .HasDefaultValue(string.Empty);

        builder.Property(c => c.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(c => c.SuiteId);
    }
}
```

- [ ] **Step 3: `EvalRunConfiguration`**

```csharp
// src/OrchestAI.Infrastructure/Data/Configurations/EvalRunConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalRunConfiguration : IEntityTypeConfiguration<EvalRun>
{
    public void Configure(EntityTypeBuilder<EvalRun> builder)
    {
        builder.ToTable("EvalRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.SuiteId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.TriggeredAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(EvalRunStatus.Pending)
            .HasConversion<string>();

        builder.Property(r => r.BaselineRunId)
            .HasColumnType("uuid");

        builder.Property(r => r.SubjectVersion)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.ErrorMessage);

        builder.Property(r => r.CompletedAt)
            .HasColumnType("timestamptz");

        builder.HasOne(r => r.Suite)
            .WithMany()
            .HasForeignKey(r => r.SuiteId)
            .OnDelete(DeleteBehavior.Cascade);

        // Self-referencing FK to the baseline run — restrict delete so a baseline can't be
        // dropped out from under a run that still points at it (the comparison would silently
        // lose meaning). Deleting the *dependent* (newer) run is unaffected.
        builder.HasOne(r => r.BaselineRun)
            .WithMany()
            .HasForeignKey(r => r.BaselineRunId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.SuiteId);
        builder.HasIndex(r => r.Status);
    }
}
```

- [ ] **Step 4: `EvalResultConfiguration`**

`AgentExecutionId` is nullable with no `HasOne`/FK relationship configured — deliberately, per ADR-011 Decision 3 / ADR-012: it's a loose correlation reference to `AgentExecution.Id` (a stable PK), not `OrchestrationTask.TraceId` (a correlation string, not a PK). Nullable so Week 9 post-hoc scoring can attach one later without a schema change, and enforcing referential integrity here would be backwards since a future post-hoc-scored `AgentExecution` may belong to a production task with no `EvalRun` in its ancestry at all.

```csharp
// src/OrchestAI.Infrastructure/Data/Configurations/EvalResultConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrchestAI.Domain.Entities;

namespace OrchestAI.Infrastructure.Data.Configurations;

public sealed class EvalResultConfiguration : IEntityTypeConfiguration<EvalResult>
{
    public void Configure(EntityTypeBuilder<EvalResult> builder)
    {
        builder.ToTable("EvalResults");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.EvalRunId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.EvalCaseId)
            .IsRequired()
            .HasColumnType("uuid");

        builder.Property(r => r.AgentExecutionId)
            .HasColumnType("uuid");

        builder.Property(r => r.ScorerType)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion<string>();

        builder.Property(r => r.ScorerVersion)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.Score)
            .IsRequired()
            .HasColumnType("decimal(5,4)");

        builder.Property(r => r.Passed)
            .IsRequired();

        builder.Property(r => r.ScorerOutput)
            .IsRequired()
            .HasColumnType("jsonb");

        builder.Property(r => r.ScoredAt)
            .IsRequired()
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("NOW()");

        builder.HasOne(r => r.Run)
            .WithMany()
            .HasForeignKey(r => r.EvalRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Case)
            .WithMany()
            .HasForeignKey(r => r.EvalCaseId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.EvalRunId, r.EvalCaseId })
            .IsUnique();

        builder.HasIndex(r => r.AgentExecutionId);
    }
}
```

- [ ] **Step 5: Update `CostLedgerConfiguration`**

```csharp
// src/OrchestAI.Infrastructure/Data/Configurations/CostLedgerConfiguration.cs — add after
// the AgentExecutionId property mapping:
        builder.Property(cl => cl.Source)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(CostSource.Production)
            .HasConversion<string>();

        builder.Property(cl => cl.EvalRunId)
            .HasColumnType("uuid");
```

Add `using OrchestAI.Domain.Enums;` to the top of the file. Also add, alongside the existing `HasIndex(cl => new { cl.OrchestrationTaskId, cl.RecordedAt })`:

```csharp
        builder.HasIndex(cl => cl.Source);
        builder.HasIndex(cl => cl.EvalRunId);
```

(No FK relationship for `EvalRunId` here either, for the same reason as `EvalResult.AgentExecutionId` above — `CostLedger` must remain writable even if the `EvalRuns` table row is later deleted, per the `OnDelete` choices already established for `AgentExecutionId` (`SetNull`). Since `EvalRunId` has no navigation configured, there's nothing to cascade — it's a plain nullable column.)

- [ ] **Step 6: Update `AgentExecutionConfiguration`**

```csharp
// src/OrchestAI.Infrastructure/Data/Configurations/AgentExecutionConfiguration.cs — add
// after the MemoriesInjectedCount property mapping:
        builder.Property(e => e.EvalRunId)
            .HasColumnType("uuid");
```

And add an index alongside the existing ones:

```csharp
        builder.HasIndex(e => e.EvalRunId);
```

- [ ] **Step 7: Register everything in `AppDbContext`**

```csharp
// src/OrchestAI.Infrastructure/Data/AppDbContext.cs — add DbSets:
    public DbSet<EvalSuite> EvalSuites => Set<EvalSuite>();
    public DbSet<EvalCase> EvalCases => Set<EvalCase>();
    public DbSet<EvalRun> EvalRuns => Set<EvalRun>();
    public DbSet<EvalResult> EvalResults => Set<EvalResult>();

// and register the configurations in OnModelCreating:
        modelBuilder.ApplyConfiguration(new EvalSuiteConfiguration());
        modelBuilder.ApplyConfiguration(new EvalCaseConfiguration());
        modelBuilder.ApplyConfiguration(new EvalRunConfiguration());
        modelBuilder.ApplyConfiguration(new EvalResultConfiguration());
```

- [ ] **Step 8: Build**

Run: `dotnet build src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings. (This step only compiles the model — it does not touch the database; Task 7 generates the migration.)

- [ ] **Step 9: Commit**

```bash
git add src/OrchestAI.Infrastructure/Data/Configurations/EvalSuiteConfiguration.cs src/OrchestAI.Infrastructure/Data/Configurations/EvalCaseConfiguration.cs src/OrchestAI.Infrastructure/Data/Configurations/EvalRunConfiguration.cs src/OrchestAI.Infrastructure/Data/Configurations/EvalResultConfiguration.cs src/OrchestAI.Infrastructure/Data/Configurations/CostLedgerConfiguration.cs src/OrchestAI.Infrastructure/Data/Configurations/AgentExecutionConfiguration.cs src/OrchestAI.Infrastructure/Data/AppDbContext.cs
git commit -m "feat: EF Core configurations for the eval/scoring data model"
```

---

## Task 7: Generate and apply the EF Core migration; seed the Eval System User

**Files:**
- Create: `src/OrchestAI.Infrastructure/Migrations/*_AddEvalScoringModel.cs` (generated)
- Modify: `src/OrchestAI.Infrastructure/Data/DatabaseSeeder.cs`

**Interfaces:**
- Consumes: the full model from Task 6.
- Produces: `DatabaseSeeder.EvalSystemUserId` — consumed by Task 19 (background worker, as the `UserId` for every eval-invocation `OrchestrationTask`).

- [ ] **Step 1: Generate the migration**

Run (from repo root, with the Postgres container from `docker-compose.yml` running):
```bash
docker compose up -d postgres
dotnet ef migrations add AddEvalScoringModel \
  --project src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj \
  --startup-project src/OrchestAI.API/OrchestAI.API.csproj
```
Expected: a new `*_AddEvalScoringModel.cs` + `.Designer.cs` under `src/OrchestAI.Infrastructure/Migrations/`, and `AppDbContextModelSnapshot.cs` updated. Inspect the generated `Up()` method — confirm it creates `EvalSuites`, `EvalCases`, `EvalRuns`, `EvalResults` tables and adds `Source`/`EvalRunId` to `CostLedger` and `EvalRunId` to `AgentExecutions`, matching Task 6's configuration.

- [ ] **Step 2: Add the Eval System User to `DatabaseSeeder`**

```csharp
// src/OrchestAI.Infrastructure/Data/DatabaseSeeder.cs — add alongside DevUserId:
    public static readonly Guid EvalSystemUserId = Guid.Parse("00000000-0000-0000-0000-0000ee7a1000");

// in SeedAsync, after the dev-user insert block, add:
        var evalUserRowsAffected = await _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "Users" ("Id", "Email", "DisplayName", "CreatedAt", "UpdatedAt")
            VALUES ({0}, {1}, {2}, {3}, {4})
            ON CONFLICT ("Id") DO NOTHING
            """,
            [EvalSystemUserId, "eval-system@orchestai.local", "Eval System", now, now],
            cancellationToken).ConfigureAwait(false);

        if (evalUserRowsAffected > 0)
        {
            _logger.LogInformation(
                "Seeded eval system user {UserId} (eval-system@orchestai.local)", EvalSystemUserId);
        }
```

- [ ] **Step 3: Apply the migration and re-seed**

Run:
```bash
dotnet ef database update \
  --project src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj \
  --startup-project src/OrchestAI.API/OrchestAI.API.csproj
dotnet run --project src/OrchestAI.API
```
Expected: migration applies cleanly; API startup log shows `Seeded eval system user ...` on first run (idempotent — no log line on subsequent runs since the `ON CONFLICT DO NOTHING` makes `rowsAffected` 0).

- [ ] **Step 4: Verify schema by hand**

Run: `docker compose exec postgres psql -U postgres -d orchestai -c "\d \"EvalResults\""`
Expected: shows `AgentExecutionId uuid`, `Score numeric(5,4)`, `Passed boolean`, unique index on `(EvalRunId, EvalCaseId)`.

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Infrastructure/Migrations/ src/OrchestAI.Infrastructure/Data/DatabaseSeeder.cs
git commit -m "feat: add EF Core migration for eval/scoring model, seed eval system user"
```

---

## Task 8: Exclude `Source=Eval` rows from cost rollups and the live dashboard

**Files:**
- Modify: `src/OrchestAI.Infrastructure/Repositories/CostLedgerRepository.cs`
- Modify: `tests/OrchestAI.Tests/OrchestAI.Tests.csproj`
- Test: `tests/OrchestAI.Tests/Infrastructure/CostLedgerRepositoryEvalFilterTests.cs`

**Interfaces:**
- Consumes: `CostSource` (Task 1), `CostLedger.Source` (Task 3/6).
- Produces: nothing new — this is the "payoff" test proving `GetDailyAggregatesAsync` (the single method feeding both `CostRollupBackgroundService` and `GetCostDashboardHandler`'s live-today branch) never returns eval-tagged cost.

`GetDailyAggregatesAsync` has no existing test coverage at the repository level (all current tests mock `ICostLedgerRepository`), so this is the first repository-level test in the project — it needs the EF Core InMemory provider, added here as a test-only package.

- [ ] **Step 1: Add the EF Core InMemory package to the test project**

```xml
<!-- tests/OrchestAI.Tests/OrchestAI.Tests.csproj — add alongside the other PackageReferences -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" Version="8.0.11" />
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Infrastructure/CostLedgerRepositoryEvalFilterTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;

namespace OrchestAI.Tests.Infrastructure;

public sealed class CostLedgerRepositoryEvalFilterTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    [Fact]
    public async Task GetDailyAggregatesAsync_MixOfProductionAndEvalRows_OnlyReturnsProduction()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var userId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            var user = TestUserFactory.Create("cost-filter@test.local");
            var task = OrchestrationTask.Create(user.Id, "t", "prompt");
            var prodExecution = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            var evalExecution = AgentExecution.Create(task.Id, AgentType.Research, "prompt", evalRunId: Guid.NewGuid());

            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(prodExecution, evalExecution);
            seedCtx.CostLedger.AddRange(
                CostLedger.Create(task.Id, "anthropic/model", 100, 50, 0.01m, prodExecution.Id),
                CostLedger.Create(
                    task.Id, "anthropic/model", 999, 999, 9.99m, evalExecution.Id,
                    source: CostSource.Eval, evalRunId: evalExecution.EvalRunId));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new CostLedgerRepository(factory);
        var aggregates = await repository.GetDailyAggregatesAsync(today, today, CancellationToken.None);

        aggregates.Should().ContainSingle();
        aggregates[0].UserId.Should().Be(userId);
        aggregates[0].InputTokens.Should().Be(100);
        aggregates[0].CostUsd.Should().Be(0.01m);
    }
}
```

Note: `aggregates[0].UserId.Should().Be(userId)` requires the seeded `User.Id` to equal the local `userId` variable — adjust the test to capture the actual created user's `Id` instead of a separate `Guid.NewGuid()`, e.g. declare `Guid userId = default;` and assign `userId = user.Id;` inside the seeding block before disposal, or simply assert `aggregates[0].UserId.Should().NotBeEmpty()` and drop the unused `userId` local. Use whichever reads more clearly when writing the file — the `TestUserFactory` helper referenced above does not exist yet; add it as a small internal static helper in the same test file:

```csharp
internal static class TestUserFactory
{
    public static User Create(string email)
    {
        var user = (User)Activator.CreateInstance(typeof(User), nonPublic: true)!;
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, Guid.NewGuid());
        typeof(User).GetProperty(nameof(User.Email))!.SetValue(user, email);
        typeof(User).GetProperty(nameof(User.DisplayName))!.SetValue(user, "Test User");
        typeof(User).GetProperty(nameof(User.CreatedAt))!.SetValue(user, DateTimeOffset.UtcNow);
        typeof(User).GetProperty(nameof(User.UpdatedAt))!.SetValue(user, DateTimeOffset.UtcNow);
        return user;
    }
}
```

Reflection is unusual for this codebase but `User` has no public constructor and no `Create` factory (unlike every other entity) — this is the minimal way to seed one from a test without adding a factory method to production code that nothing else needs.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter CostLedgerRepositoryEvalFilterTests`
Expected: FAIL — the eval-tagged row is currently included, so the aggregate sums `100+999=1099` input tokens instead of `100`, and/or the query throws because `CostSource`/`Source` don't exist as query predicates yet (they do exist by this point in the plan from Task 3/6 — the actual failure here is that `GetDailyAggregatesAsync` has no `Source` filter yet, so `aggregates` has 2 rows or 1 merged row with the wrong sums).

- [ ] **Step 4: Add the filter**

```csharp
// src/OrchestAI.Infrastructure/Repositories/CostLedgerRepository.cs — in GetDailyAggregatesAsync,
// change the initial Where clause to also exclude eval-sourced rows:
        var raw = await ctx.CostLedger
            .Where(c => c.RecordedAt >= fromUtc && c.RecordedAt < toUtc
                && c.AgentExecutionId != null && c.Source == CostSource.Production)
            .Join(ctx.OrchestrationTasks, c => c.OrchestrationTaskId, t => t.Id,
                (c, t) => new { c.RecordedAt, t.UserId, c.AgentExecutionId, c.Model, c.InputTokens, c.OutputTokens, c.CostUsd })
            .Join(ctx.AgentExecutions, x => x.AgentExecutionId!.Value, e => e.Id,
                (x, e) => new { x.RecordedAt, x.UserId, e.AgentType, x.Model, x.InputTokens, x.OutputTokens, x.CostUsd })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
```

Add `using OrchestAI.Domain.Enums;` to the top of the file.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter CostLedgerRepositoryEvalFilterTests`
Expected: PASS

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: all pass, including `CostRollupBackgroundServiceTests` and `GetCostDashboardHandlerTests` (both mock `ICostLedgerRepository` directly, so the new `Source` filter inside the real repository doesn't affect them — this new test is the only one that exercises the real query).

- [ ] **Step 7: Commit**

```bash
git add src/OrchestAI.Infrastructure/Repositories/CostLedgerRepository.cs tests/OrchestAI.Tests/OrchestAI.Tests.csproj tests/OrchestAI.Tests/Infrastructure/CostLedgerRepositoryEvalFilterTests.cs
git commit -m "feat: exclude eval-sourced cost rows from rollup/dashboard aggregates"
```

---

## Task 9: `IEvalScorer` contract, `EvalScoreResult`, `EvalScoringContext`, `EvalOptions`

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IEvalScorer.cs`
- Create: `src/OrchestAI.Domain/Models/EvalScoreResult.cs`
- Create: `src/OrchestAI.Domain/Models/EvalScoringContext.cs`
- Create: `src/OrchestAI.Infrastructure/Configuration/EvalOptions.cs`

**Interfaces:**
- Consumes: `EvalCase`, `EvalScorerType` (Tasks 1/4).
- Produces: `IEvalScorer.ScoreAsync(EvalCase, string actualOutput, EvalScoringContext, CancellationToken) -> Task<EvalScoreResult>`; `EvalScoringContext(Guid OrchestrationTaskId, Guid EvalRunId)`; `EvalScoreResult(decimal Score, bool Passed, string ScorerVersion, string ScorerOutputJson)` — consumed by Tasks 10, 11, 12, 19.

- [ ] **Step 1: `EvalScoringContext` and `EvalScoreResult`**

```csharp
// src/OrchestAI.Domain/Models/EvalScoringContext.cs
namespace OrchestAI.Domain.Models;

// Carries what a scorer needs beyond the case/output pair to attribute its own cost correctly —
// specifically, LlmJudgeScorer's judge call must write a CostLedger row tagged Source=Eval and
// linked back to the triggering EvalRun, and CostLedger.OrchestrationTaskId is NOT NULL, so the
// per-case OrchestrationTaskId the live invocation already created must travel with the score
// request. RuleBasedScorer ignores this entirely. See ADR-012.
public sealed record EvalScoringContext(Guid OrchestrationTaskId, Guid EvalRunId);
```

```csharp
// src/OrchestAI.Domain/Models/EvalScoreResult.cs
namespace OrchestAI.Domain.Models;

public sealed record EvalScoreResult(
    decimal Score,
    bool Passed,
    string ScorerVersion,
    string ScorerOutputJson
);
```

- [ ] **Step 2: `IEvalScorer`**

```csharp
// src/OrchestAI.Domain/Interfaces/IEvalScorer.cs
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Models;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalScorer
{
    EvalScorerType ScorerType { get; }

    Task<EvalScoreResult> ScoreAsync(
        EvalCase evalCase,
        string actualOutput,
        EvalScoringContext context,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: `EvalOptions`**

```csharp
// src/OrchestAI.Infrastructure/Configuration/EvalOptions.cs
namespace OrchestAI.Infrastructure.Configuration;

public sealed class EvalOptions
{
    public const string SectionName = "Eval";

    // "provider/model", same qualified format as AgentOptions.Models — e.g.
    // "anthropic/claude-haiku-4-5-20251001". A cheap, fast model is deliberately the default:
    // judge calls happen once per eval case per run, at real provider cost (see ADR-012).
    public string JudgeModel { get; init; } = "anthropic/claude-haiku-4-5-20251001";

    // Used when an EvalCase's ExpectedCriteria JSON for an LlmJudge case omits its own
    // "passThreshold" — see RuleBasedScorer/LlmJudgeScorer's ExpectedCriteria shape.
    public decimal DefaultJudgePassThreshold { get; init; } = 0.7m;
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalScorer.cs src/OrchestAI.Domain/Models/EvalScoreResult.cs src/OrchestAI.Domain/Models/EvalScoringContext.cs src/OrchestAI.Infrastructure/Configuration/EvalOptions.cs
git commit -m "feat: add IEvalScorer contract and eval configuration options"
```

---

## Task 10: `RuleBasedScorer`

**Files:**
- Create: `src/OrchestAI.Infrastructure/Eval/RuleBasedScorer.cs`
- Test: `tests/OrchestAI.Tests/Infrastructure/RuleBasedScorerTests.cs`

**Interfaces:**
- Consumes: `IEvalScorer` (Task 9).
- Produces: `RuleBasedScorer` (registered as `IEvalScorer` in Task 12's factory). `ExpectedCriteria` JSON shape: `{"mode":"ExactMatch","expected":"..."}` | `{"mode":"Regex","pattern":"..."}` | `{"mode":"JsonSchema","schema":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}}`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/OrchestAI.Tests/Infrastructure/RuleBasedScorerTests.cs
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class RuleBasedScorerTests
{
    private static readonly EvalScoringContext Context = new(Guid.NewGuid(), Guid.NewGuid());

    private static EvalCase BuildCase(string expectedCriteria) => EvalCase.Create(
        Guid.NewGuid(), "{}", expectedCriteria, EvalScorerType.RuleBased, regressionThreshold: 0.1m);

    [Theory]
    [InlineData("hello world", "hello world", true)]
    [InlineData("hello world", "goodbye", false)]
    public async Task ScoreAsync_ExactMatch_ComparesActualOutputVerbatim(
        string actual, string expected, bool shouldPass)
    {
        var scorer = new RuleBasedScorer();
        var evalCase = BuildCase($$"""{"mode":"ExactMatch","expected":"{{expected}}"}""");

        var result = await scorer.ScoreAsync(evalCase, actual, Context, CancellationToken.None);

        result.Passed.Should().Be(shouldPass);
        result.Score.Should().Be(shouldPass ? 1.0m : 0.0m);
    }

    [Theory]
    [InlineData("order-42891", @"^order-\d+$", true)]
    [InlineData("not-an-order", @"^order-\d+$", false)]
    public async Task ScoreAsync_Regex_MatchesPattern(string actual, string pattern, bool shouldPass)
    {
        var scorer = new RuleBasedScorer();
        var evalCase = BuildCase($$"""{"mode":"Regex","pattern":"{{pattern}}"}""");

        var result = await scorer.ScoreAsync(evalCase, actual, Context, CancellationToken.None);

        result.Passed.Should().Be(shouldPass);
    }

    [Fact]
    public async Task ScoreAsync_JsonSchema_RequiredPropertyMissing_Fails()
    {
        var scorer = new RuleBasedScorer();
        var evalCase = BuildCase(
            """{"mode":"JsonSchema","schema":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}}""");

        var result = await scorer.ScoreAsync(evalCase, """{"age":30}""", Context, CancellationToken.None);

        result.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task ScoreAsync_JsonSchema_RequiredPropertyPresentWithCorrectType_Passes()
    {
        var scorer = new RuleBasedScorer();
        var evalCase = BuildCase(
            """{"mode":"JsonSchema","schema":{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}}""");

        var result = await scorer.ScoreAsync(evalCase, """{"name":"Ada"}""", Context, CancellationToken.None);

        result.Passed.Should().BeTrue();
        result.Score.Should().Be(1.0m);
    }

    [Fact]
    public async Task ScoreAsync_AlwaysStampsScorerVersion()
    {
        var scorer = new RuleBasedScorer();
        var evalCase = BuildCase("""{"mode":"ExactMatch","expected":"x"}""");

        var result = await scorer.ScoreAsync(evalCase, "x", Context, CancellationToken.None);

        result.ScorerVersion.Should().Be(RuleBasedScorer.Version);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter RuleBasedScorerTests`
Expected: FAIL — `RuleBasedScorer` doesn't exist, compile error.

- [ ] **Step 3: Implement `RuleBasedScorer`**

```csharp
// src/OrchestAI.Infrastructure/Eval/RuleBasedScorer.cs
using System.Text.Json;
using System.Text.RegularExpressions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;

namespace OrchestAI.Infrastructure.Eval;

// Deterministic scoring for tool-call/format correctness — exact string match, regex, or a
// deliberately minimal JSON Schema check (required-property + primitive-type validation only;
// this is not a full JSON Schema draft implementation, matching the same lightweight schema
// shape already used by ToolInputSchema for MCP tool definitions rather than pulling in a new
// schema-validation dependency for Week 8's scope).
public sealed class RuleBasedScorer : IEvalScorer
{
    public const string Version = "rule-based-v1";

    public EvalScorerType ScorerType => EvalScorerType.RuleBased;

    public Task<EvalScoreResult> ScoreAsync(
        EvalCase evalCase,
        string actualOutput,
        EvalScoringContext context,
        CancellationToken cancellationToken = default)
    {
        using var criteria = JsonDocument.Parse(evalCase.ExpectedCriteria);
        var mode = criteria.RootElement.GetProperty("mode").GetString();

        var (passed, detail) = mode switch
        {
            "ExactMatch" => ScoreExactMatch(criteria.RootElement, actualOutput),
            "Regex" => ScoreRegex(criteria.RootElement, actualOutput),
            "JsonSchema" => ScoreJsonSchema(criteria.RootElement, actualOutput),
            _ => throw new InvalidOperationException($"Unknown RuleBasedScorer mode '{mode}'.")
        };

        var score = passed ? 1.0m : 0.0m;
        var output = JsonSerializer.Serialize(new { mode, passed, detail });

        return Task.FromResult(new EvalScoreResult(score, passed, Version, output));
    }

    private static (bool Passed, string Detail) ScoreExactMatch(JsonElement criteria, string actualOutput)
    {
        var expected = criteria.GetProperty("expected").GetString() ?? string.Empty;
        var passed = string.Equals(actualOutput, expected, StringComparison.Ordinal);
        return (passed, passed ? "exact match" : $"expected '{expected}'");
    }

    private static (bool Passed, string Detail) ScoreRegex(JsonElement criteria, string actualOutput)
    {
        var pattern = criteria.GetProperty("pattern").GetString() ?? string.Empty;
        var passed = Regex.IsMatch(actualOutput, pattern);
        return (passed, passed ? "pattern matched" : $"did not match /{pattern}/");
    }

    private static (bool Passed, string Detail) ScoreJsonSchema(JsonElement criteria, string actualOutput)
    {
        JsonDocument actualDoc;
        try
        {
            actualDoc = JsonDocument.Parse(actualOutput);
        }
        catch (JsonException)
        {
            return (false, "actual output is not valid JSON");
        }

        using (actualDoc)
        {
            var schema = criteria.GetProperty("schema");
            var properties = schema.TryGetProperty("properties", out var p) ? p : default;
            var required = schema.TryGetProperty("required", out var r)
                ? r.EnumerateArray().Select(e => e.GetString()!).ToList()
                : [];

            foreach (var requiredProp in required)
            {
                if (!actualDoc.RootElement.TryGetProperty(requiredProp, out var actualValue))
                    return (false, $"missing required property '{requiredProp}'");

                if (properties.ValueKind == JsonValueKind.Object
                    && properties.TryGetProperty(requiredProp, out var propSchema)
                    && propSchema.TryGetProperty("type", out var typeEl)
                    && !MatchesJsonType(actualValue, typeEl.GetString()!))
                {
                    return (false, $"property '{requiredProp}' does not match declared type");
                }
            }

            return (true, "schema satisfied");
        }
    }

    private static bool MatchesJsonType(JsonElement value, string jsonType) => jsonType switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        _ => true
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter RuleBasedScorerTests`
Expected: PASS, all 8 test cases.

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Infrastructure/Eval/RuleBasedScorer.cs tests/OrchestAI.Tests/Infrastructure/RuleBasedScorerTests.cs
git commit -m "feat: add RuleBasedScorer (exact match, regex, minimal JSON schema)"
```

---

## Task 11: `LlmJudgeScorer`

**Files:**
- Create: `src/OrchestAI.Infrastructure/Eval/LlmJudgeScorer.cs`
- Test: `tests/OrchestAI.Tests/Infrastructure/LlmJudgeScorerTests.cs`

**Interfaces:**
- Consumes: `IEvalScorer`/`EvalScoringContext`/`EvalScoreResult` (Task 9), `ILlmProviderFactory`/`ILlmProvider`/`AgentConversation.Temperature` (existing + Task 2), `IModelPricingCache` (existing), `ICostLedgerRepository` (existing), `EvalOptions` (Task 9).
- Produces: `LlmJudgeScorer` (registered as `IEvalScorer` in Task 12). `ExpectedCriteria` JSON shape for `LlmJudge` cases: `{"rubric":"...","passThreshold":0.7}` (`passThreshold` optional, falls back to `EvalOptions.DefaultJudgePassThreshold`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/OrchestAI.Tests/Infrastructure/LlmJudgeScorerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class LlmJudgeScorerTests
{
    private static EvalCase BuildCase(string criteriaJson = """{"rubric":"Does it greet the user?"}""") =>
        EvalCase.Create(Guid.NewGuid(), "{}", criteriaJson, EvalScorerType.LlmJudge, regressionThreshold: 0.1m);

    private static (LlmJudgeScorer Scorer, Mock<ILlmProvider> Provider, Mock<ICostLedgerRepository> CostRepo)
        Build(string judgeResponseJson)
    {
        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", judgeResponseJson, [], 200, 40));

        var factoryMock = new Mock<ILlmProviderFactory>();
        factoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock
            .Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));

        var costRepoMock = new Mock<ICostLedgerRepository>();
        costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001",
            DefaultJudgePassThreshold = 0.7m
        });

        var scorer = new LlmJudgeScorer(factoryMock.Object, pricingCacheMock.Object, costRepoMock.Object, options);
        return (scorer, providerMock, costRepoMock);
    }

    [Fact]
    public async Task ScoreAsync_JudgeReturnsScoreAboveThreshold_Passes()
    {
        var (scorer, _, _) = Build("""{"score":0.9,"reasoning":"Greets the user warmly."}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await scorer.ScoreAsync(BuildCase(), "Hello there!", context, CancellationToken.None);

        result.Score.Should().Be(0.9m);
        result.Passed.Should().BeTrue();
        result.ScorerVersion.Should().Be(LlmJudgeScorer.Version);
    }

    [Fact]
    public async Task ScoreAsync_JudgeReturnsScoreBelowThreshold_Fails()
    {
        var (scorer, _, _) = Build("""{"score":0.3,"reasoning":"No greeting present."}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());

        var result = await scorer.ScoreAsync(BuildCase(), "The weather is nice.", context, CancellationToken.None);

        result.Passed.Should().BeFalse();
    }

    [Fact]
    public async Task ScoreAsync_UsesCaseSpecificPassThresholdWhenPresent()
    {
        var (scorer, _, _) = Build("""{"score":0.5,"reasoning":"Partially meets rubric."}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());
        var evalCase = BuildCase("""{"rubric":"x","passThreshold":0.4}""");

        var result = await scorer.ScoreAsync(evalCase, "some output", context, CancellationToken.None);

        result.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task ScoreAsync_AlwaysSendsTemperatureZero()
    {
        var (scorer, providerMock, _) = Build("""{"score":0.8,"reasoning":"ok"}""");
        var context = new EvalScoringContext(Guid.NewGuid(), Guid.NewGuid());

        await scorer.ScoreAsync(BuildCase(), "output", context, CancellationToken.None);

        providerMock.Verify(
            p => p.SendAsync(It.Is<AgentConversation>(c => c.Temperature == 0.0), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScoreAsync_WritesCostLedgerRowTaggedEvalAndLinkedToRun()
    {
        var (scorer, _, costRepoMock) = Build("""{"score":0.8,"reasoning":"ok"}""");
        var orchestrationTaskId = Guid.NewGuid();
        var evalRunId = Guid.NewGuid();
        var context = new EvalScoringContext(orchestrationTaskId, evalRunId);

        CostLedger? captured = null;
        costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Callback<CostLedger, CancellationToken>((l, _) => captured = l)
            .Returns(Task.CompletedTask);

        await scorer.ScoreAsync(BuildCase(), "output", context, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Source.Should().Be(CostSource.Eval);
        captured.EvalRunId.Should().Be(evalRunId);
        captured.OrchestrationTaskId.Should().Be(orchestrationTaskId);
        captured.AgentExecutionId.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter LlmJudgeScorerTests`
Expected: FAIL — `LlmJudgeScorer` doesn't exist, compile error.

- [ ] **Step 3: Implement `LlmJudgeScorer`**

```csharp
// src/OrchestAI.Infrastructure/Eval/LlmJudgeScorer.cs
using System.Text.Json;
using Microsoft.Extensions.Options;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Configuration;

namespace OrchestAI.Infrastructure.Eval;

// Non-deterministic scoring via an LLM judge. Temperature is pinned to 0 for every call —
// per ADR-012 this makes scores *directionally* reliable across runs, not bit-exact
// reproducible; that's a documented, accepted limitation, not a bug. The judge call itself is
// cost-tracked exactly like an agent turn (same ModelPricing lookup, same CostLedger row shape)
// but tagged Source=Eval/EvalRunId so it never reaches production cost dashboards — see
// CostLedgerRepository.GetDailyAggregatesAsync's Source filter (Task 8).
public sealed class LlmJudgeScorer : IEvalScorer
{
    public const string Version = "llm-judge-v1";

    private const string JudgeSystemPrompt =
        """
        You are an evaluation judge. Given a rubric and an agent's actual output, score how well
        the output satisfies the rubric from 0.0 (fails completely) to 1.0 (fully satisfies it).
        Return ONLY valid JSON, no markdown:
        {"score": 0.0, "reasoning": "one sentence explaining the score"}
        """;

    private readonly ILlmProviderFactory _providerFactory;
    private readonly IModelPricingCache _pricingCache;
    private readonly ICostLedgerRepository _costLedgerRepository;
    private readonly IOptions<EvalOptions> _options;

    public LlmJudgeScorer(
        ILlmProviderFactory providerFactory,
        IModelPricingCache pricingCache,
        ICostLedgerRepository costLedgerRepository,
        IOptions<EvalOptions> options)
    {
        _providerFactory = providerFactory;
        _pricingCache = pricingCache;
        _costLedgerRepository = costLedgerRepository;
        _options = options;
    }

    public EvalScorerType ScorerType => EvalScorerType.LlmJudge;

    public async Task<EvalScoreResult> ScoreAsync(
        EvalCase evalCase,
        string actualOutput,
        EvalScoringContext context,
        CancellationToken cancellationToken = default)
    {
        using var criteria = JsonDocument.Parse(evalCase.ExpectedCriteria);
        var rubric = criteria.RootElement.GetProperty("rubric").GetString() ?? string.Empty;
        var passThreshold = criteria.RootElement.TryGetProperty("passThreshold", out var thresholdEl)
            ? thresholdEl.GetDecimal()
            : _options.Value.DefaultJudgePassThreshold;

        var modelRef = ModelRef.Parse(_options.Value.JudgeModel);
        var provider = _providerFactory.Resolve(modelRef.ProviderId);

        var conversation = new AgentConversation(
            JudgeSystemPrompt,
            Messages: [new ConversationMessage("user", $"Rubric: {rubric}\n\nActual output:\n{actualOutput}")],
            Tools: [],
            Model: modelRef.ModelName,
            MaxTokens: 256,
            Temperature: 0.0);

        var turn = await provider.SendAsync(conversation, cancellationToken).ConfigureAwait(false);
        var (score, reasoning) = ParseJudgeResponse(turn.Text);
        var passed = score >= passThreshold;

        var costUsd = await CalculateCostAsync(modelRef.ModelName, turn.InputTokens, turn.OutputTokens, cancellationToken)
            .ConfigureAwait(false);

        var ledger = CostLedger.Create(
            context.OrchestrationTaskId,
            _options.Value.JudgeModel,
            turn.InputTokens, turn.OutputTokens, costUsd,
            agentExecutionId: null,
            source: CostSource.Eval,
            evalRunId: context.EvalRunId);
        await _costLedgerRepository.AddAsync(ledger, cancellationToken).ConfigureAwait(false);

        var outputJson = JsonSerializer.Serialize(new { score, reasoning, passThreshold });
        return new EvalScoreResult(score, passed, Version, outputJson);
    }

    private static (decimal Score, string Reasoning) ParseJudgeResponse(string judgeText)
    {
        var cleaned = judgeText.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var start = cleaned.IndexOf('\n') + 1;
            var end = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (end > start) cleaned = cleaned[start..end].Trim();
        }

        using var doc = JsonDocument.Parse(cleaned);
        var score = doc.RootElement.GetProperty("score").GetDecimal();
        var reasoning = doc.RootElement.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
        return (score, reasoning);
    }

    private async Task<decimal> CalculateCostAsync(
        string model, int inputTokens, int outputTokens, CancellationToken cancellationToken)
    {
        var pricing = await _pricingCache.GetAsync(model, cancellationToken).ConfigureAwait(false);
        if (pricing is null) return 0m;

        return (inputTokens / 1_000_000m) * pricing.InputPerMillion
             + (outputTokens / 1_000_000m) * pricing.OutputPerMillion;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter LlmJudgeScorerTests`
Expected: PASS, all 5 tests.

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Infrastructure/Eval/LlmJudgeScorer.cs tests/OrchestAI.Tests/Infrastructure/LlmJudgeScorerTests.cs
git commit -m "feat: add LlmJudgeScorer, temperature-pinned and cost-tagged as eval"
```

---

## Task 12: `IEvalScorerFactory`

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IEvalScorerFactory.cs`
- Create: `src/OrchestAI.Infrastructure/Eval/EvalScorerFactory.cs`
- Test: `tests/OrchestAI.Tests/Infrastructure/EvalScorerFactoryTests.cs`

**Interfaces:**
- Consumes: `IEvalScorer` implementations (Tasks 10, 11).
- Produces: `IEvalScorerFactory.Resolve(EvalScorerType) -> IEvalScorer` — consumed by Task 19 (background worker). DI registration deferred to Task 24 (mirrors how `AgentFactory` is defined here but wired up in `DependencyInjection.cs` separately).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Infrastructure/EvalScorerFactoryTests.cs
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalScorerFactoryTests
{
    private sealed class FakeScorer(EvalScorerType type) : IEvalScorer
    {
        public EvalScorerType ScorerType => type;
        public Task<EvalScoreResult> ScoreAsync(
            EvalCase evalCase, string actualOutput, EvalScoringContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EvalScoreResult(1.0m, true, "fake", "{}"));
    }

    [Theory]
    [InlineData(EvalScorerType.RuleBased)]
    [InlineData(EvalScorerType.LlmJudge)]
    public void Resolve_KnownScorerType_ReturnsMatchingScorer(EvalScorerType type)
    {
        var factory = new EvalScorerFactory([new FakeScorer(EvalScorerType.RuleBased), new FakeScorer(EvalScorerType.LlmJudge)]);

        var scorer = factory.Resolve(type);

        scorer.ScorerType.Should().Be(type);
    }

    [Fact]
    public void Resolve_UnknownScorerType_Throws()
    {
        var factory = new EvalScorerFactory([new FakeScorer(EvalScorerType.RuleBased)]);

        var act = () => factory.Resolve(EvalScorerType.LlmJudge);

        act.Should().Throw<InvalidOperationException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalScorerFactoryTests`
Expected: FAIL — `IEvalScorerFactory`/`EvalScorerFactory` don't exist.

- [ ] **Step 3: Implement**

```csharp
// src/OrchestAI.Domain/Interfaces/IEvalScorerFactory.cs
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalScorerFactory
{
    IEvalScorer Resolve(EvalScorerType scorerType);
}
```

```csharp
// src/OrchestAI.Infrastructure/Eval/EvalScorerFactory.cs
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Eval;

public sealed class EvalScorerFactory : IEvalScorerFactory
{
    private readonly IReadOnlyDictionary<EvalScorerType, IEvalScorer> _scorersByType;

    public EvalScorerFactory(IEnumerable<IEvalScorer> scorers)
    {
        _scorersByType = scorers.ToDictionary(s => s.ScorerType);
    }

    public IEvalScorer Resolve(EvalScorerType scorerType) =>
        _scorersByType.TryGetValue(scorerType, out var scorer)
            ? scorer
            : throw new InvalidOperationException($"No IEvalScorer registered for '{scorerType}'.");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalScorerFactoryTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalScorerFactory.cs src/OrchestAI.Infrastructure/Eval/EvalScorerFactory.cs tests/OrchestAI.Tests/Infrastructure/EvalScorerFactoryTests.cs
git commit -m "feat: add IEvalScorerFactory to resolve scorers by EvalCase.ScorerType"
```

---

## Task 13: `IEvalSuiteRepository`

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IEvalSuiteRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/EvalSuiteRepository.cs`

**Interfaces:**
- Consumes: `EvalSuite`, `EvalCase` (Task 4).
- Produces: `AddAsync(EvalSuite)`, `AddCaseAsync(EvalCase)`, `GetByIdAsync(Guid)`, `GetByIdWithCasesAsync(Guid)`, `GetAllAsync()` — consumed by Tasks 16, 17, 19, 20.

- [ ] **Step 1: Interface**

```csharp
// src/OrchestAI.Domain/Interfaces/IEvalSuiteRepository.cs
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalSuiteRepository
{
    Task<EvalSuite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<EvalSuite?> GetByIdWithCasesAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EvalSuite>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(EvalSuite suite, CancellationToken cancellationToken = default);
    Task AddCaseAsync(EvalCase evalCase, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Implementation** (mirrors `CostRollupRepository`'s `IDbContextFactory`-per-call pattern)

```csharp
// src/OrchestAI.Infrastructure/Repositories/EvalSuiteRepository.cs
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class EvalSuiteRepository : IEvalSuiteRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EvalSuiteRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<EvalSuite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalSuites.FirstOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EvalSuite?> GetByIdWithCasesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalSuites
            .Include(s => s.Cases)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EvalSuite>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalSuites
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(EvalSuite suite, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.EvalSuites.AddAsync(suite, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddCaseAsync(EvalCase evalCase, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.EvalCases.AddAsync(evalCase, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalSuiteRepository.cs src/OrchestAI.Infrastructure/Repositories/EvalSuiteRepository.cs
git commit -m "feat: add IEvalSuiteRepository"
```

---

## Task 14: `IEvalRunRepository`

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IEvalRunRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/EvalRunRepository.cs`

**Interfaces:**
- Consumes: `EvalRun` (Task 5).
- Produces: `AddAsync`, `UpdateAsync`, `GetByIdAsync`, `GetBySuiteIdAsync` — consumed by Tasks 18, 19, 20, 22.

- [ ] **Step 1: Interface**

```csharp
// src/OrchestAI.Domain/Interfaces/IEvalRunRepository.cs
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalRunRepository
{
    Task<EvalRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    // Most recent runs for a suite, newest first — powers the baseline-run dropdown.
    Task<IReadOnlyList<EvalRun>> GetBySuiteIdAsync(
        Guid suiteId, CancellationToken cancellationToken = default);

    Task AddAsync(EvalRun run, CancellationToken cancellationToken = default);
    Task UpdateAsync(EvalRun run, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Implementation**

```csharp
// src/OrchestAI.Infrastructure/Repositories/EvalRunRepository.cs
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class EvalRunRepository : IEvalRunRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EvalRunRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<EvalRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalRuns.FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<EvalRun>> GetBySuiteIdAsync(
        Guid suiteId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalRuns
            .Where(r => r.SuiteId == suiteId)
            .OrderByDescending(r => r.TriggeredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(EvalRun run, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.EvalRuns.AddAsync(run, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(EvalRun run, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        ctx.EvalRuns.Update(run);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalRunRepository.cs src/OrchestAI.Infrastructure/Repositories/EvalRunRepository.cs
git commit -m "feat: add IEvalRunRepository"
```

---

## Task 15: `IEvalResultRepository`

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IEvalResultRepository.cs`
- Create: `src/OrchestAI.Infrastructure/Repositories/EvalResultRepository.cs`

**Interfaces:**
- Consumes: `EvalResult` (Task 5).
- Produces: `AddAsync`, `GetByRunIdAsync` — consumed by Tasks 19, 21, 22.

- [ ] **Step 1: Interface**

```csharp
// src/OrchestAI.Domain/Interfaces/IEvalResultRepository.cs
using OrchestAI.Domain.Entities;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalResultRepository
{
    Task<IReadOnlyList<EvalResult>> GetByRunIdAsync(Guid evalRunId, CancellationToken cancellationToken = default);
    Task AddAsync(EvalResult result, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Implementation**

```csharp
// src/OrchestAI.Infrastructure/Repositories/EvalResultRepository.cs
using Microsoft.EntityFrameworkCore;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Repositories;

public sealed class EvalResultRepository : IEvalResultRepository
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;

    public EvalResultRepository(IDbContextFactory<AppDbContext> contextFactory)
        => _contextFactory = contextFactory;

    public async Task<IReadOnlyList<EvalResult>> GetByRunIdAsync(
        Guid evalRunId, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalResults
            .Where(r => r.EvalRunId == evalRunId)
            .OrderBy(r => r.ScoredAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(EvalResult result, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await ctx.EvalResults.AddAsync(result, cancellationToken).ConfigureAwait(false);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalResultRepository.cs src/OrchestAI.Infrastructure/Repositories/EvalResultRepository.cs
git commit -m "feat: add IEvalResultRepository"
```

---

## Task 16: `CreateEvalSuiteCommand`

**Files:**
- Create: `src/OrchestAI.Application/Commands/CreateEvalSuite/CreateEvalSuiteCommand.cs`
- Create: `src/OrchestAI.Application/Commands/CreateEvalSuite/CreateEvalSuiteResponse.cs`
- Create: `src/OrchestAI.Application/Commands/CreateEvalSuite/CreateEvalSuiteHandler.cs`
- Test: `tests/OrchestAI.Tests/Application/CreateEvalSuiteHandlerTests.cs`

**Interfaces:**
- Consumes: `IEvalSuiteRepository` (Task 13), `AgentType` (existing), `ValidationException` (existing).
- Produces: `CreateEvalSuiteCommand(string Name, string Description, AgentType TargetAgentType) : IRequest<CreateEvalSuiteResponse>` — consumed by Task 23 (controller).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Application/CreateEvalSuiteHandlerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.CreateEvalSuite;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class CreateEvalSuiteHandlerTests
{
    [Fact]
    public async Task Handle_ValidRequest_PersistsSuiteAndReturnsResponse()
    {
        var repoMock = new Mock<IEvalSuiteRepository>();
        EvalSuite? captured = null;
        repoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalSuite>(), It.IsAny<CancellationToken>()))
            .Callback<EvalSuite, CancellationToken>((s, _) => captured = s)
            .Returns(Task.CompletedTask);

        var handler = new CreateEvalSuiteHandler(repoMock.Object, NullLogger<CreateEvalSuiteHandler>.Instance);

        var response = await handler.Handle(
            new CreateEvalSuiteCommand("Research smoke suite", "Basic research agent checks", AgentType.Research),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Name.Should().Be("Research smoke suite");
        response.TargetAgentType.Should().Be("Research");
    }

    [Fact]
    public async Task Handle_EmptyName_ThrowsValidationException()
    {
        var repoMock = new Mock<IEvalSuiteRepository>();
        var handler = new CreateEvalSuiteHandler(repoMock.Object, NullLogger<CreateEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new CreateEvalSuiteCommand("", "desc", AgentType.Research), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter CreateEvalSuiteHandlerTests`
Expected: FAIL — command/handler don't exist.

- [ ] **Step 3: Implement**

```csharp
// src/OrchestAI.Application/Commands/CreateEvalSuite/CreateEvalSuiteCommand.cs
using MediatR;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Commands.CreateEvalSuite;

public sealed record CreateEvalSuiteCommand(
    string Name,
    string Description,
    AgentType TargetAgentType
) : IRequest<CreateEvalSuiteResponse>;
```

```csharp
// src/OrchestAI.Application/Commands/CreateEvalSuite/CreateEvalSuiteResponse.cs
namespace OrchestAI.Application.Commands.CreateEvalSuite;

public sealed record CreateEvalSuiteResponse(
    Guid Id,
    string Name,
    string Description,
    string TargetAgentType,
    DateTimeOffset CreatedAt
);
```

```csharp
// src/OrchestAI.Application/Commands/CreateEvalSuite/CreateEvalSuiteHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.CreateEvalSuite;

public sealed class CreateEvalSuiteHandler : IRequestHandler<CreateEvalSuiteCommand, CreateEvalSuiteResponse>
{
    private readonly IEvalSuiteRepository _repository;
    private readonly ILogger<CreateEvalSuiteHandler> _logger;

    public CreateEvalSuiteHandler(IEvalSuiteRepository repository, ILogger<CreateEvalSuiteHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<CreateEvalSuiteResponse> Handle(
        CreateEvalSuiteCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException(nameof(request.Name), "Name is required.");

        var suite = EvalSuite.Create(request.Name, request.Description, request.TargetAgentType);
        await _repository.AddAsync(suite, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Created eval suite {SuiteId} '{Name}' targeting {AgentType}",
            suite.Id, suite.Name, suite.TargetAgentType);

        return new CreateEvalSuiteResponse(
            suite.Id, suite.Name, suite.Description, suite.TargetAgentType.ToString(), suite.CreatedAt);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter CreateEvalSuiteHandlerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Application/Commands/CreateEvalSuite/ tests/OrchestAI.Tests/Application/CreateEvalSuiteHandlerTests.cs
git commit -m "feat: add CreateEvalSuiteCommand"
```

---

## Task 17: `AddEvalCaseCommand`

**Files:**
- Create: `src/OrchestAI.Application/Commands/AddEvalCase/AddEvalCaseCommand.cs`
- Create: `src/OrchestAI.Application/Commands/AddEvalCase/AddEvalCaseResponse.cs`
- Create: `src/OrchestAI.Application/Commands/AddEvalCase/AddEvalCaseHandler.cs`
- Test: `tests/OrchestAI.Tests/Application/AddEvalCaseHandlerTests.cs`

**Interfaces:**
- Consumes: `IEvalSuiteRepository` (Task 13), `EvalScorerType` (Task 1).
- Produces: `AddEvalCaseCommand(Guid SuiteId, string InputPayloadJson, string ExpectedCriteriaJson, EvalScorerType ScorerType, decimal RegressionThreshold, string Tags) : IRequest<AddEvalCaseResponse>` — consumed by Task 23 (controller).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Application/AddEvalCaseHandlerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.AddEvalCase;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class AddEvalCaseHandlerTests
{
    [Fact]
    public async Task Handle_SuiteExists_PersistsCaseUnderIt()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var repoMock = new Mock<IEvalSuiteRepository>();
        repoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        EvalCase? captured = null;
        repoMock
            .Setup(r => r.AddCaseAsync(It.IsAny<EvalCase>(), It.IsAny<CancellationToken>()))
            .Callback<EvalCase, CancellationToken>((c, _) => captured = c)
            .Returns(Task.CompletedTask);

        var handler = new AddEvalCaseHandler(repoMock.Object, NullLogger<AddEvalCaseHandler>.Instance);

        var response = await handler.Handle(
            new AddEvalCaseCommand(
                suite.Id, "{\"prompt\":\"hi\"}", "{\"mode\":\"ExactMatch\",\"expected\":\"hi\"}",
                EvalScorerType.RuleBased, 0.05m, "smoke"),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.SuiteId.Should().Be(suite.Id);
        response.SuiteId.Should().Be(suite.Id);
    }

    [Fact]
    public async Task Handle_SuiteDoesNotExist_ThrowsNotFoundException()
    {
        var repoMock = new Mock<IEvalSuiteRepository>();
        repoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvalSuite?)null);

        var handler = new AddEvalCaseHandler(repoMock.Object, NullLogger<AddEvalCaseHandler>.Instance);

        var act = async () => await handler.Handle(
            new AddEvalCaseCommand(Guid.NewGuid(), "{}", "{}", EvalScorerType.RuleBased, 0.05m, ""),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter AddEvalCaseHandlerTests`
Expected: FAIL — command/handler don't exist.

- [ ] **Step 3: Implement**

```csharp
// src/OrchestAI.Application/Commands/AddEvalCase/AddEvalCaseCommand.cs
using MediatR;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Commands.AddEvalCase;

public sealed record AddEvalCaseCommand(
    Guid SuiteId,
    string InputPayloadJson,
    string ExpectedCriteriaJson,
    EvalScorerType ScorerType,
    decimal RegressionThreshold,
    string Tags
) : IRequest<AddEvalCaseResponse>;
```

```csharp
// src/OrchestAI.Application/Commands/AddEvalCase/AddEvalCaseResponse.cs
namespace OrchestAI.Application.Commands.AddEvalCase;

public sealed record AddEvalCaseResponse(
    Guid Id,
    Guid SuiteId,
    string ScorerType,
    decimal RegressionThreshold,
    DateTimeOffset CreatedAt
);
```

```csharp
// src/OrchestAI.Application/Commands/AddEvalCase/AddEvalCaseHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.AddEvalCase;

public sealed class AddEvalCaseHandler : IRequestHandler<AddEvalCaseCommand, AddEvalCaseResponse>
{
    private readonly IEvalSuiteRepository _suiteRepository;
    private readonly ILogger<AddEvalCaseHandler> _logger;

    public AddEvalCaseHandler(IEvalSuiteRepository suiteRepository, ILogger<AddEvalCaseHandler> logger)
    {
        _suiteRepository = suiteRepository;
        _logger = logger;
    }

    public async Task<AddEvalCaseResponse> Handle(AddEvalCaseCommand request, CancellationToken cancellationToken)
    {
        var suite = await _suiteRepository.GetByIdAsync(request.SuiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalSuite), request.SuiteId);

        if (request.RegressionThreshold < 0)
            throw new ValidationException(
                nameof(request.RegressionThreshold), "RegressionThreshold must not be negative.");

        var evalCase = EvalCase.Create(
            suite.Id, request.InputPayloadJson, request.ExpectedCriteriaJson,
            request.ScorerType, request.RegressionThreshold, request.Tags);

        await _suiteRepository.AddCaseAsync(evalCase, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Added eval case {CaseId} to suite {SuiteId} (scorer={ScorerType})",
            evalCase.Id, suite.Id, evalCase.ScorerType);

        return new AddEvalCaseResponse(
            evalCase.Id, evalCase.SuiteId, evalCase.ScorerType.ToString(),
            evalCase.RegressionThreshold, evalCase.CreatedAt);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter AddEvalCaseHandlerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Application/Commands/AddEvalCase/ tests/OrchestAI.Tests/Application/AddEvalCaseHandlerTests.cs
git commit -m "feat: add AddEvalCaseCommand"
```

---

## Task 18: `IEvalRunQueue` and `RunEvalSuiteCommand`

**Files:**
- Create: `src/OrchestAI.Domain/Interfaces/IEvalRunQueue.cs`
- Create: `src/OrchestAI.Infrastructure/Eval/InMemoryEvalRunQueue.cs`
- Create: `src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteCommand.cs`
- Create: `src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteResponse.cs`
- Create: `src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs`
- Test: `tests/OrchestAI.Tests/Application/RunEvalSuiteHandlerTests.cs`

**Interfaces:**
- Consumes: `IEvalSuiteRepository` (Task 13), `IEvalRunRepository` (Task 14).
- Produces: `IEvalRunQueue.EnqueueAsync(Guid evalRunId)` / `DequeueAsync(CancellationToken) -> Task<Guid>` (Channel-backed, mirrors `InMemoryOrchestrationEventBus`'s use of `System.Threading.Channels`); `RunEvalSuiteCommand(Guid SuiteId, string SubjectVersion, Guid? BaselineRunId) : IRequest<RunEvalSuiteResponse>` — consumed by Task 19 (background worker dequeues) and Task 23 (controller).

`RunEvalSuiteHandler` creates the `EvalRun` row (`Status=Pending`) and enqueues its id; it does **not** execute anything itself — per the spec, scoring must not run synchronously inline with the HTTP request. Execution happens in Task 19's `EvalRunBackgroundWorker`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Application/RunEvalSuiteHandlerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Commands.RunEvalSuite;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class RunEvalSuiteHandlerTests
{
    [Fact]
    public async Task Handle_SuiteExists_CreatesPendingRunAndEnqueuesIt()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        EvalRun? captured = null;
        runRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()))
            .Callback<EvalRun, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = new RunEvalSuiteHandler(
            suiteRepoMock.Object, runRepoMock.Object, queueMock.Object, NullLogger<RunEvalSuiteHandler>.Instance);

        var response = await handler.Handle(
            new RunEvalSuiteCommand(suite.Id, "commit-abc123", BaselineRunId: null), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Status.Should().Be(EvalRunStatus.Pending);
        captured.SubjectVersion.Should().Be("commit-abc123");
        response.EvalRunId.Should().Be(captured.Id);
        queueMock.Verify(q => q.EnqueueAsync(captured.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_SuiteDoesNotExist_ThrowsNotFoundException()
    {
        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EvalSuite?)null);

        var handler = new RunEvalSuiteHandler(
            suiteRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>(),
            NullLogger<RunEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new RunEvalSuiteCommand(Guid.NewGuid(), "v1", null), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_EmptySubjectVersion_ThrowsValidationException()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var handler = new RunEvalSuiteHandler(
            suiteRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>(),
            NullLogger<RunEvalSuiteHandler>.Instance);

        var act = async () => await handler.Handle(
            new RunEvalSuiteCommand(suite.Id, "", null), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter RunEvalSuiteHandlerTests`
Expected: FAIL — `IEvalRunQueue`/command/handler don't exist.

- [ ] **Step 3: `IEvalRunQueue` + in-memory implementation**

```csharp
// src/OrchestAI.Domain/Interfaces/IEvalRunQueue.cs
namespace OrchestAI.Domain.Interfaces;

public interface IEvalRunQueue
{
    Task EnqueueAsync(Guid evalRunId, CancellationToken cancellationToken = default);
    Task<Guid> DequeueAsync(CancellationToken cancellationToken = default);
}
```

```csharp
// src/OrchestAI.Infrastructure/Eval/InMemoryEvalRunQueue.cs
using System.Threading.Channels;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Infrastructure.Eval;

// Same primitive InMemoryOrchestrationEventBus uses (System.Threading.Channels) — an
// unbounded, single-process work queue. Good enough for Week 8's "don't run scoring inline
// with the HTTP request" requirement; a durable queue is out of scope until eval volume
// justifies surviving a process restart.
public sealed class InMemoryEvalRunQueue : IEvalRunQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public async Task EnqueueAsync(Guid evalRunId, CancellationToken cancellationToken = default) =>
        await _channel.Writer.WriteAsync(evalRunId, cancellationToken).ConfigureAwait(false);

    public async Task<Guid> DequeueAsync(CancellationToken cancellationToken = default) =>
        await _channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
}
```

- [ ] **Step 4: `RunEvalSuiteCommand`/`Response`**

```csharp
// src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteCommand.cs
using MediatR;

namespace OrchestAI.Application.Commands.RunEvalSuite;

public sealed record RunEvalSuiteCommand(
    Guid SuiteId,
    string SubjectVersion,
    Guid? BaselineRunId
) : IRequest<RunEvalSuiteResponse>;
```

```csharp
// src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteResponse.cs
namespace OrchestAI.Application.Commands.RunEvalSuite;

public sealed record RunEvalSuiteResponse(
    Guid EvalRunId,
    Guid SuiteId,
    string Status,
    Guid? BaselineRunId,
    DateTimeOffset TriggeredAt
);
```

- [ ] **Step 5: `RunEvalSuiteHandler`**

```csharp
// src/OrchestAI.Application/Commands/RunEvalSuite/RunEvalSuiteHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Commands.RunEvalSuite;

public sealed class RunEvalSuiteHandler : IRequestHandler<RunEvalSuiteCommand, RunEvalSuiteResponse>
{
    private readonly IEvalSuiteRepository _suiteRepository;
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalRunQueue _queue;
    private readonly ILogger<RunEvalSuiteHandler> _logger;

    public RunEvalSuiteHandler(
        IEvalSuiteRepository suiteRepository,
        IEvalRunRepository runRepository,
        IEvalRunQueue queue,
        ILogger<RunEvalSuiteHandler> logger)
    {
        _suiteRepository = suiteRepository;
        _runRepository = runRepository;
        _queue = queue;
        _logger = logger;
    }

    public async Task<RunEvalSuiteResponse> Handle(RunEvalSuiteCommand request, CancellationToken cancellationToken)
    {
        var suite = await _suiteRepository.GetByIdAsync(request.SuiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalSuite), request.SuiteId);

        if (string.IsNullOrWhiteSpace(request.SubjectVersion))
            throw new ValidationException(nameof(request.SubjectVersion), "SubjectVersion is required.");

        var run = EvalRun.Create(suite.Id, request.SubjectVersion, request.BaselineRunId);
        await _runRepository.AddAsync(run, cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(run.Id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Enqueued eval run {RunId} for suite {SuiteId} (subject={SubjectVersion}, baseline={BaselineRunId})",
            run.Id, suite.Id, request.SubjectVersion, request.BaselineRunId);

        return new RunEvalSuiteResponse(run.Id, suite.Id, run.Status.ToString(), run.BaselineRunId, run.TriggeredAt);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter RunEvalSuiteHandlerTests`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalRunQueue.cs src/OrchestAI.Infrastructure/Eval/InMemoryEvalRunQueue.cs src/OrchestAI.Application/Commands/RunEvalSuite/ tests/OrchestAI.Tests/Application/RunEvalSuiteHandlerTests.cs
git commit -m "feat: add RunEvalSuiteCommand, enqueues execution instead of running inline"
```

---

## Task 19: `EvalRunBackgroundWorker`

**Files:**
- Create: `src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs`
- Test: `tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerTests.cs`

**Interfaces:**
- Consumes: `IEvalRunQueue` (Task 18), `IEvalRunRepository` (Task 14), `IEvalSuiteRepository` (Task 13), `IEvalResultRepository` (Task 15), `IOrchestrationTaskRepository`/`IAgentFactory` (existing), `IEvalScorerFactory` (Task 12), `DatabaseSeeder.EvalSystemUserId` (Task 7).
- Produces: `EvalRunBackgroundWorker` (registered via `AddHostedService` in Task 24) — this is the component that satisfies the spec's end-to-end integration test requirement.

Same shape as `CostRollupBackgroundService`: a `BackgroundService` that creates a fresh DI scope per unit of work (here, per dequeued run) so scoped repositories/`IAgentFactory` resolve correctly from a singleton hosted service. One eval case's failure (agent invocation throws, scorer throws) must not abort the rest of the run — caught and recorded per case, mirroring `StartOrchestrationHandler.RunSubAgentAsync`'s try/catch.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerTests.cs
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalRunBackgroundWorkerTests
{
    private sealed class StubAgent(AgentExecutionResult result) : IAgent
    {
        public Task<AgentExecutionResult> ExecuteAsync(
            Guid orchestrationTaskId, Guid userId, string userPrompt,
            CancellationToken cancellationToken = default, string? parentSpanId = null, Guid? evalRunId = null) =>
            Task.FromResult(result);
    }

    [Fact]
    public async Task ProcessRunAsync_StubAgentSucceeds_PersistsEvalResultLinkedToAgentExecutionId()
    {
        var suite = EvalSuite.Create("Research suite", "desc", AgentType.Research);
        var evalCase = EvalCase.Create(
            suite.Id, "{\"prompt\":\"hi\"}", "{\"mode\":\"ExactMatch\",\"expected\":\"hello\"}",
            EvalScorerType.RuleBased, regressionThreshold: 0.1m);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { evalCase });

        var run = EvalRun.Create(suite.Id, "commit-abc123", baselineRunId: null);
        var stubExecutionId = Guid.NewGuid();
        var stubAgent = new StubAgent(new AgentExecutionResult(stubExecutionId, "hello", true, 10, 5, 0.001m));

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdWithCasesAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(stubAgent);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        EvalResult? captured = null;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback<EvalResult, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var worker = BuildWorker(
            suiteRepoMock.Object, runRepoMock.Object, resultRepoMock.Object, taskRepoMock.Object, agentFactoryMock.Object);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.Status.Should().Be(EvalRunStatus.Completed);
        captured.Should().NotBeNull();
        captured!.AgentExecutionId.Should().Be(stubExecutionId);
        captured.EvalCaseId.Should().Be(evalCase.Id);
        captured.Passed.Should().BeFalse(); // actual "hello" vs criteria expecting exact "hello" — see note below
    }

    private static EvalRunBackgroundWorker BuildWorker(
        IEvalSuiteRepository suiteRepo, IEvalRunRepository runRepo, IEvalResultRepository resultRepo,
        IOrchestrationTaskRepository taskRepo, IAgentFactory agentFactory)
    {
        var services = new ServiceCollection();
        services.AddSingleton(suiteRepo);
        services.AddSingleton(runRepo);
        services.AddSingleton(resultRepo);
        services.AddSingleton(taskRepo);
        services.AddSingleton(agentFactory);
        services.AddSingleton<IEvalScorerFactory>(new Eval.EvalScorerFactory([new Eval.RuleBasedScorer()]));
        var provider = services.BuildServiceProvider();

        var queueMock = new Mock<IEvalRunQueue>();
        return new EvalRunBackgroundWorker(
            queueMock.Object, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EvalRunBackgroundWorker>.Instance);
    }
}
```

`captured.Passed.Should().BeFalse()` looks surprising at first glance — the case criteria is `{"mode":"ExactMatch","expected":"hello"}` and the stub agent's output is literally `"hello"`, so `RuleBasedScorer` will actually score it `Passed=true`. Correct the assertion before running:

```csharp
        captured.Passed.Should().BeTrue();
        captured.Score.Should().Be(1.0m);
```

(`typeof(EvalSuite).GetField("_cases", ...)` is reflection into the private backing field — `EvalSuite` has no public way to attach a case in-memory without a repository round-trip, mirroring the same reflection trick used for `TestUserFactory` in Task 8. This is test-only scaffolding, not a production concern.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalRunBackgroundWorkerTests`
Expected: FAIL — `EvalRunBackgroundWorker` doesn't exist.

- [ ] **Step 3: Implement**

```csharp
// src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Infrastructure.Data;

namespace OrchestAI.Infrastructure.Eval;

// Dequeues EvalRun ids enqueued by RunEvalSuiteHandler and executes them — the eval-layer
// counterpart to CostRollupBackgroundService, but triggered by a queued unit of work instead
// of a fixed polling interval. One case failing (agent throws, scorer throws) must not abort
// the rest of the run, mirroring StartOrchestrationHandler.RunSubAgentAsync's per-agent
// try/catch.
public sealed class EvalRunBackgroundWorker : BackgroundService
{
    private readonly IEvalRunQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EvalRunBackgroundWorker> _logger;

    public EvalRunBackgroundWorker(
        IEvalRunQueue queue, IServiceScopeFactory scopeFactory, ILogger<EvalRunBackgroundWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid evalRunId;
            try
            {
                evalRunId = await _queue.DequeueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ProcessRunAsync(evalRunId, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Eval run {RunId} processing failed unexpectedly", evalRunId);
            }
        }
    }

    internal async Task ProcessRunAsync(Guid evalRunId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<IEvalRunRepository>();
        var suiteRepository = scope.ServiceProvider.GetRequiredService<IEvalSuiteRepository>();
        var resultRepository = scope.ServiceProvider.GetRequiredService<IEvalResultRepository>();
        var taskRepository = scope.ServiceProvider.GetRequiredService<IOrchestrationTaskRepository>();
        var agentFactory = scope.ServiceProvider.GetRequiredService<IAgentFactory>();
        var scorerFactory = scope.ServiceProvider.GetRequiredService<IEvalScorerFactory>();

        var run = await runRepository.GetByIdAsync(evalRunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("Eval run {RunId} not found, skipping", evalRunId);
            return;
        }

        var suite = await suiteRepository.GetByIdWithCasesAsync(run.SuiteId, cancellationToken).ConfigureAwait(false);
        if (suite is null || suite.Cases.Count == 0)
        {
            run.MarkFailed(suite is null ? "suite no longer exists" : "suite has no cases");
            await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            return;
        }

        run.MarkRunning();
        await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        foreach (var evalCase in suite.Cases)
        {
            try
            {
                await ProcessCaseAsync(
                    run, suite, evalCase, taskRepository, agentFactory, scorerFactory, resultRepository,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Eval case {CaseId} in run {RunId} failed unexpectedly, continuing", evalCase.Id, run.Id);
            }
        }

        run.MarkCompleted();
        await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Eval run {RunId} completed ({CaseCount} cases)", run.Id, suite.Cases.Count);
    }

    private async Task ProcessCaseAsync(
        EvalRun run,
        EvalSuite suite,
        EvalCase evalCase,
        IOrchestrationTaskRepository taskRepository,
        IAgentFactory agentFactory,
        IEvalScorerFactory scorerFactory,
        IEvalResultRepository resultRepository,
        CancellationToken cancellationToken)
    {
        var task = OrchestrationTask.Create(
            DatabaseSeeder.EvalSystemUserId, $"Eval run {run.Id} / case {evalCase.Id}", evalCase.InputPayload);
        await taskRepository.AddAsync(task, cancellationToken).ConfigureAwait(false);
        task.MarkRunning();
        await taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        var agent = agentFactory.Create(suite.TargetAgentType);
        var result = await agent.ExecuteAsync(
            task.Id, DatabaseSeeder.EvalSystemUserId, evalCase.InputPayload, cancellationToken,
            parentSpanId: null, evalRunId: run.Id).ConfigureAwait(false);

        if (result.Success)
            task.MarkCompleted(result.Output);
        else
            task.MarkFailed(result.ErrorMessage ?? "unknown error");
        await taskRepository.UpdateAsync(task, cancellationToken).ConfigureAwait(false);

        var evalResult = result.Success
            ? await ScoreSuccessAsync(run, evalCase, result, scorerFactory, task.Id, cancellationToken).ConfigureAwait(false)
            : EvalResult.Create(
                run.Id, evalCase.Id, result.AgentExecutionId == Guid.Empty ? null : result.AgentExecutionId,
                evalCase.ScorerType, "invocation-failed", score: 0m, passed: false,
                scorerOutput: JsonSerializer.Serialize(new { error = result.ErrorMessage }));

        await resultRepository.AddAsync(evalResult, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<EvalResult> ScoreSuccessAsync(
        EvalRun run, EvalCase evalCase, AgentExecutionResult result, IEvalScorerFactory scorerFactory,
        Guid orchestrationTaskId, CancellationToken cancellationToken)
    {
        var scorer = scorerFactory.Resolve(evalCase.ScorerType);
        var scoreResult = await scorer.ScoreAsync(
            evalCase, result.Output, new EvalScoringContext(orchestrationTaskId, run.Id), cancellationToken)
            .ConfigureAwait(false);

        return EvalResult.Create(
            run.Id, evalCase.Id, result.AgentExecutionId, evalCase.ScorerType,
            scoreResult.ScorerVersion, scoreResult.Score, scoreResult.Passed, scoreResult.ScorerOutputJson);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter EvalRunBackgroundWorkerTests`
Expected: PASS

- [ ] **Step 5: Add a second test for the per-case failure path**

```csharp
// tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerTests.cs — add:
    [Fact]
    public async Task ProcessRunAsync_AgentInvocationFails_RecordsFailedEvalResultWithoutThrowing()
    {
        var suite = EvalSuite.Create("Research suite", "desc", AgentType.Research);
        var evalCase = EvalCase.Create(
            suite.Id, "{}", "{\"mode\":\"ExactMatch\",\"expected\":\"x\"}",
            EvalScorerType.RuleBased, regressionThreshold: 0.1m);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { evalCase });

        var run = EvalRun.Create(suite.Id, "commit-abc123", null);
        var failedExecutionId = Guid.NewGuid();
        var stubAgent = new StubAgent(new AgentExecutionResult(
            failedExecutionId, string.Empty, false, 0, 0, 0m, ErrorMessage: "provider timed out"));

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdWithCasesAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var taskRepoMock = new Mock<IOrchestrationTaskRepository>();
        taskRepoMock.Setup(r => r.AddAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        taskRepoMock.Setup(r => r.UpdateAsync(It.IsAny<OrchestrationTask>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var agentFactoryMock = new Mock<IAgentFactory>();
        agentFactoryMock.Setup(f => f.Create(AgentType.Research)).Returns(stubAgent);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        EvalResult? captured = null;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback<EvalResult, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var worker = BuildWorker(
            suiteRepoMock.Object, runRepoMock.Object, resultRepoMock.Object, taskRepoMock.Object, agentFactoryMock.Object);

        var act = async () => await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        await act.Should().NotThrowAsync();
        run.Status.Should().Be(EvalRunStatus.Completed);
        captured.Should().NotBeNull();
        captured!.Passed.Should().BeFalse();
        captured.Score.Should().Be(0m);
        captured.AgentExecutionId.Should().Be(failedExecutionId);
    }
```

Run: `dotnet test tests/OrchestAI.Tests --filter EvalRunBackgroundWorkerTests`
Expected: PASS, both tests.

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerTests.cs
git commit -m "feat: add EvalRunBackgroundWorker to execute suites and score results"
```

---

## Task 20: `GetEvalSuitesQuery` and `GetEvalRunsQuery`

**Files:**
- Create: `src/OrchestAI.Application/Queries/GetEvalSuites/GetEvalSuitesQuery.cs`
- Create: `src/OrchestAI.Application/Queries/GetEvalSuites/GetEvalSuitesResponse.cs`
- Create: `src/OrchestAI.Application/Queries/GetEvalSuites/GetEvalSuitesHandler.cs`
- Create: `src/OrchestAI.Application/Queries/GetEvalRuns/GetEvalRunsQuery.cs`
- Create: `src/OrchestAI.Application/Queries/GetEvalRuns/GetEvalRunsResponse.cs`
- Create: `src/OrchestAI.Application/Queries/GetEvalRuns/GetEvalRunsHandler.cs`
- Test: `tests/OrchestAI.Tests/Application/GetEvalRunsHandlerTests.cs`

**Interfaces:**
- Consumes: `IEvalSuiteRepository` (Task 13), `IEvalRunRepository` (Task 14).
- Produces: suite list for the frontend's "list suites" view, run list for the baseline-run dropdown — both consumed by Task 23 (controller) and Task 25 (frontend).

These are thin, list-only reads (no new domain logic), so only `GetEvalRunsQuery` gets a dedicated test here — `GetEvalSuitesQuery` is exercised end-to-end by Task 23's controller wiring and doesn't warrant a duplicate handler test for a straight passthrough.

- [ ] **Step 1: `GetEvalSuitesQuery`**

```csharp
// src/OrchestAI.Application/Queries/GetEvalSuites/GetEvalSuitesQuery.cs
using MediatR;

namespace OrchestAI.Application.Queries.GetEvalSuites;

public sealed record GetEvalSuitesQuery : IRequest<GetEvalSuitesResponse>;
```

```csharp
// src/OrchestAI.Application/Queries/GetEvalSuites/GetEvalSuitesResponse.cs
namespace OrchestAI.Application.Queries.GetEvalSuites;

public sealed record GetEvalSuitesResponse(IReadOnlyList<EvalSuiteSummaryDto> Suites);

public sealed record EvalSuiteSummaryDto(
    Guid Id, string Name, string Description, string TargetAgentType, DateTimeOffset CreatedAt);
```

```csharp
// src/OrchestAI.Application/Queries/GetEvalSuites/GetEvalSuitesHandler.cs
using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetEvalSuites;

public sealed class GetEvalSuitesHandler : IRequestHandler<GetEvalSuitesQuery, GetEvalSuitesResponse>
{
    private readonly IEvalSuiteRepository _repository;

    public GetEvalSuitesHandler(IEvalSuiteRepository repository) => _repository = repository;

    public async Task<GetEvalSuitesResponse> Handle(GetEvalSuitesQuery request, CancellationToken cancellationToken)
    {
        var suites = await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        return new GetEvalSuitesResponse(suites
            .Select(s => new EvalSuiteSummaryDto(s.Id, s.Name, s.Description, s.TargetAgentType.ToString(), s.CreatedAt))
            .ToList());
    }
}
```

- [ ] **Step 2: Write the failing test for `GetEvalRunsQuery`**

```csharp
// tests/OrchestAI.Tests/Application/GetEvalRunsHandlerTests.cs
using FluentAssertions;
using Moq;
using OrchestAI.Application.Queries.GetEvalRuns;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetEvalRunsHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsRunsNewestFirstWithStatusAndBaseline()
    {
        var suiteId = Guid.NewGuid();
        var older = EvalRun.Create(suiteId, "v1", null);
        var newer = EvalRun.Create(suiteId, "v2", older.Id);

        var repoMock = new Mock<IEvalRunRepository>();
        repoMock
            .Setup(r => r.GetBySuiteIdAsync(suiteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([newer, older]);

        var handler = new GetEvalRunsHandler(repoMock.Object);

        var response = await handler.Handle(new GetEvalRunsQuery(suiteId), CancellationToken.None);

        response.Runs.Should().HaveCount(2);
        response.Runs[0].Id.Should().Be(newer.Id);
        response.Runs[0].BaselineRunId.Should().Be(older.Id);
        response.Runs[1].Id.Should().Be(older.Id);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter GetEvalRunsHandlerTests`
Expected: FAIL — query/handler don't exist.

- [ ] **Step 4: `GetEvalRunsQuery`**

```csharp
// src/OrchestAI.Application/Queries/GetEvalRuns/GetEvalRunsQuery.cs
using MediatR;

namespace OrchestAI.Application.Queries.GetEvalRuns;

public sealed record GetEvalRunsQuery(Guid SuiteId) : IRequest<GetEvalRunsResponse>;
```

```csharp
// src/OrchestAI.Application/Queries/GetEvalRuns/GetEvalRunsResponse.cs
namespace OrchestAI.Application.Queries.GetEvalRuns;

public sealed record GetEvalRunsResponse(IReadOnlyList<EvalRunSummaryDto> Runs);

public sealed record EvalRunSummaryDto(
    Guid Id, string Status, string SubjectVersion, Guid? BaselineRunId,
    DateTimeOffset TriggeredAt, DateTimeOffset? CompletedAt);
```

```csharp
// src/OrchestAI.Application/Queries/GetEvalRuns/GetEvalRunsHandler.cs
using MediatR;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetEvalRuns;

public sealed class GetEvalRunsHandler : IRequestHandler<GetEvalRunsQuery, GetEvalRunsResponse>
{
    private readonly IEvalRunRepository _repository;

    public GetEvalRunsHandler(IEvalRunRepository repository) => _repository = repository;

    public async Task<GetEvalRunsResponse> Handle(GetEvalRunsQuery request, CancellationToken cancellationToken)
    {
        var runs = await _repository.GetBySuiteIdAsync(request.SuiteId, cancellationToken).ConfigureAwait(false);

        return new GetEvalRunsResponse(runs
            .Select(r => new EvalRunSummaryDto(
                r.Id, r.Status.ToString(), r.SubjectVersion, r.BaselineRunId, r.TriggeredAt, r.CompletedAt))
            .ToList());
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter GetEvalRunsHandlerTests`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Application/Queries/GetEvalSuites/ src/OrchestAI.Application/Queries/GetEvalRuns/ tests/OrchestAI.Tests/Application/GetEvalRunsHandlerTests.cs
git commit -m "feat: add GetEvalSuitesQuery and GetEvalRunsQuery"
```

---

## Task 21: `GetEvalRunResultsQuery`

**Files:**
- Create: `src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsQuery.cs`
- Create: `src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsResponse.cs`
- Create: `src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsHandler.cs`
- Test: `tests/OrchestAI.Tests/Application/GetEvalRunResultsHandlerTests.cs`

**Interfaces:**
- Consumes: `IEvalRunRepository` (Task 14), `IEvalResultRepository` (Task 15).
- Produces: per-run result list — consumed by Task 23 (controller), Task 25 (frontend results view).

This is the task that carries the spec's "once an `EvalResult.score` is persisted, re-running the report must not recompute it" regression test. The proof isn't just behavioral — `GetEvalRunResultsHandler`'s constructor takes no `IEvalScorer`/`IEvalScorerFactory` dependency at all, so there is nothing in its dependency graph capable of recomputing a score. The test demonstrates this by calling `Handle` twice against a repository stub returning a fixed, already-scored `EvalResult` and confirming the returned score never changes and no scorer is ever invoked (there being no scorer to invoke).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/OrchestAI.Tests/Application/GetEvalRunResultsHandlerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetEvalRunResults;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetEvalRunResultsHandlerTests
{
    [Fact]
    public async Task Handle_CalledTwice_ReturnsStoredScoreVerbatimBothTimes_NeverRecomputes()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "v1", null);
        var evalCaseId = Guid.NewGuid();
        var persisted = EvalResult.Create(
            run.Id, evalCaseId, Guid.NewGuid(), EvalScorerType.RuleBased, "rule-based-v1",
            score: 0.42m, passed: false, scorerOutput: "{\"note\":\"stale by design\"}");

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock
            .Setup(r => r.GetByRunIdAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([persisted]);

        var handler = new GetEvalRunResultsHandler(
            runRepoMock.Object, resultRepoMock.Object, NullLogger<GetEvalRunResultsHandler>.Instance);

        var first = await handler.Handle(new GetEvalRunResultsQuery(run.Id), CancellationToken.None);
        var second = await handler.Handle(new GetEvalRunResultsQuery(run.Id), CancellationToken.None);

        first.Results.Single().Score.Should().Be(0.42m);
        second.Results.Single().Score.Should().Be(0.42m);
        resultRepoMock.Verify(r => r.GetByRunIdAsync(run.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Handle_RunDoesNotExist_ThrowsNotFoundException()
    {
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EvalRun?)null);

        var handler = new GetEvalRunResultsHandler(
            runRepoMock.Object, Mock.Of<IEvalResultRepository>(), NullLogger<GetEvalRunResultsHandler>.Instance);

        var act = async () => await handler.Handle(new GetEvalRunResultsQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter GetEvalRunResultsHandlerTests`
Expected: FAIL — query/handler don't exist.

- [ ] **Step 3: Implement**

```csharp
// src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsQuery.cs
using MediatR;

namespace OrchestAI.Application.Queries.GetEvalRunResults;

public sealed record GetEvalRunResultsQuery(Guid EvalRunId) : IRequest<GetEvalRunResultsResponse>;
```

```csharp
// src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsResponse.cs
namespace OrchestAI.Application.Queries.GetEvalRunResults;

public sealed record GetEvalRunResultsResponse(
    Guid EvalRunId, string Status, IReadOnlyList<EvalResultDto> Results);

public sealed record EvalResultDto(
    Guid EvalCaseId, Guid? AgentExecutionId, string ScorerType, string ScorerVersion,
    decimal Score, bool Passed, string ScorerOutputJson, DateTimeOffset ScoredAt);
```

```csharp
// src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetEvalRunResults;

public sealed class GetEvalRunResultsHandler : IRequestHandler<GetEvalRunResultsQuery, GetEvalRunResultsResponse>
{
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalResultRepository _resultRepository;
    private readonly ILogger<GetEvalRunResultsHandler> _logger;

    public GetEvalRunResultsHandler(
        IEvalRunRepository runRepository, IEvalResultRepository resultRepository,
        ILogger<GetEvalRunResultsHandler> logger)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
        _logger = logger;
    }

    public async Task<GetEvalRunResultsResponse> Handle(
        GetEvalRunResultsQuery request, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetByIdAsync(request.EvalRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), request.EvalRunId);

        var results = await _resultRepository.GetByRunIdAsync(run.Id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Read {Count} eval results for run {RunId}", results.Count, run.Id);

        return new GetEvalRunResultsResponse(
            run.Id, run.Status.ToString(),
            results.Select(r => new EvalResultDto(
                r.EvalCaseId, r.AgentExecutionId, r.ScorerType.ToString(), r.ScorerVersion,
                r.Score, r.Passed, r.ScorerOutput, r.ScoredAt))
                .ToList());
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter GetEvalRunResultsHandlerTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Application/Queries/GetEvalRunResults/ tests/OrchestAI.Tests/Application/GetEvalRunResultsHandlerTests.cs
git commit -m "feat: add GetEvalRunResultsQuery, scores are read verbatim and never recomputed"
```

---

## Task 22: `GetRegressionReportQuery`

**Files:**
- Create: `src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportQuery.cs`
- Create: `src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportResponse.cs`
- Create: `src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportHandler.cs`
- Test: `tests/OrchestAI.Tests/Application/GetRegressionReportHandlerTests.cs`

**Interfaces:**
- Consumes: `IEvalRunRepository` (Task 14), `IEvalSuiteRepository` (Task 13, for per-case `RegressionThreshold`), `IEvalResultRepository` (Task 15).
- Produces: per-case score deltas + suite-level pass-rate delta, with the explicit regression pass/fail rule from the spec: *a case is flagged as regressed when its score drop exceeds that case's `regression_threshold`* — consumed by Task 23 (controller), Task 25 (frontend).

Per the spec, this must **fail clearly, not silently return an empty diff**, when `EvalRun.BaselineRunId` is null.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/OrchestAI.Tests/Application/GetRegressionReportHandlerTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetRegressionReport;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetRegressionReportHandlerTests
{
    private static (IEvalRunRepository RunRepo, IEvalSuiteRepository SuiteRepo, IEvalResultRepository ResultRepo)
        BuildRepos(
            EvalRun currentRun, EvalRun? baselineRun, EvalSuite suite,
            IReadOnlyList<EvalResult> currentResults, IReadOnlyList<EvalResult> baselineResults)
    {
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(currentRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(currentRun);
        if (baselineRun is not null)
            runRepoMock.Setup(r => r.GetByIdAsync(baselineRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(baselineRun);

        var suiteRepoMock = new Mock<IEvalSuiteRepository>();
        suiteRepoMock.Setup(r => r.GetByIdWithCasesAsync(suite.Id, It.IsAny<CancellationToken>())).ReturnsAsync(suite);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock.Setup(r => r.GetByRunIdAsync(currentRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(currentResults);
        if (baselineRun is not null)
            resultRepoMock.Setup(r => r.GetByRunIdAsync(baselineRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(baselineResults);

        return (runRepoMock.Object, suiteRepoMock.Object, resultRepoMock.Object);
    }

    private static EvalSuite BuildSuiteWithCase(out EvalCase evalCase, decimal regressionThreshold)
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        evalCase = EvalCase.Create(suite.Id, "{}", "{}", EvalScorerType.RuleBased, regressionThreshold);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { evalCase });
        return suite;
    }

    [Fact]
    public async Task Handle_ScoreDropExceedsThreshold_FlagsCaseAsRegressed()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.05m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var baselineResult = EvalResult.Create(
            baselineRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.90m, true, "{}");
        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.70m, false, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], [baselineResult]);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        var diff = response.CaseDiffs.Single();
        diff.ScoreDelta.Should().Be(0.20m);
        diff.Regressed.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ScoreDropWithinThreshold_DoesNotFlagRegression()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.30m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var baselineResult = EvalResult.Create(
            baselineRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.90m, true, "{}");
        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.70m, false, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], [baselineResult]);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        response.CaseDiffs.Single().Regressed.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_CaseHasNoBaselineResult_ReportedAsNewCaseNotRegressed()
    {
        var suite = BuildSuiteWithCase(out var evalCase, regressionThreshold: 0.05m);
        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var currentResult = EvalResult.Create(
            currentRun.Id, evalCase.Id, null, EvalScorerType.RuleBased, "rule-based-v1", 0.50m, true, "{}");

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(
            currentRun, baselineRun, suite, [currentResult], []);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        var diff = response.CaseDiffs.Single();
        diff.IsNewCase.Should().BeTrue();
        diff.Regressed.Should().BeFalse();
        diff.ScoreDelta.Should().BeNull();
    }

    [Fact]
    public async Task Handle_BaselineRunIdIsNull_ThrowsValidationExceptionInsteadOfEmptyDiff()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRunId: null);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(currentRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(currentRun);

        var handler = new GetRegressionReportHandler(
            runRepoMock.Object, Mock.Of<IEvalSuiteRepository>(), Mock.Of<IEvalResultRepository>(),
            NullLogger<GetRegressionReportHandler>.Instance);

        var act = async () => await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_ComputesSuiteLevelPassRateDelta()
    {
        var suite = EvalSuite.Create("Suite", "desc", AgentType.Research);
        var caseA = EvalCase.Create(suite.Id, "{}", "{}", EvalScorerType.RuleBased, 0.05m);
        var caseB = EvalCase.Create(suite.Id, "{}", "{}", EvalScorerType.RuleBased, 0.05m);
        typeof(EvalSuite).GetField("_cases", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(suite, new List<EvalCase> { caseA, caseB });

        var baselineRun = EvalRun.Create(suite.Id, "v1", null);
        var currentRun = EvalRun.Create(suite.Id, "v2", baselineRun.Id);

        var baselineResults = new List<EvalResult>
        {
            EvalResult.Create(baselineRun.Id, caseA.Id, null, EvalScorerType.RuleBased, "v1", 1.0m, true, "{}"),
            EvalResult.Create(baselineRun.Id, caseB.Id, null, EvalScorerType.RuleBased, "v1", 1.0m, true, "{}")
        };
        var currentResults = new List<EvalResult>
        {
            EvalResult.Create(currentRun.Id, caseA.Id, null, EvalScorerType.RuleBased, "v1", 1.0m, true, "{}"),
            EvalResult.Create(currentRun.Id, caseB.Id, null, EvalScorerType.RuleBased, "v1", 0.0m, false, "{}")
        };

        var (runRepo, suiteRepo, resultRepo) = BuildRepos(currentRun, baselineRun, suite, currentResults, baselineResults);
        var handler = new GetRegressionReportHandler(
            runRepo, suiteRepo, resultRepo, NullLogger<GetRegressionReportHandler>.Instance);

        var response = await handler.Handle(new GetRegressionReportQuery(currentRun.Id), CancellationToken.None);

        response.BaselinePassRate.Should().Be(1.0m);
        response.CurrentPassRate.Should().Be(0.5m);
        response.PassRateDelta.Should().Be(-0.5m);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter GetRegressionReportHandlerTests`
Expected: FAIL — query/handler don't exist.

- [ ] **Step 3: `GetRegressionReportQuery`/`Response`**

```csharp
// src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportQuery.cs
using MediatR;

namespace OrchestAI.Application.Queries.GetRegressionReport;

public sealed record GetRegressionReportQuery(Guid EvalRunId) : IRequest<GetRegressionReportResponse>;
```

```csharp
// src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportResponse.cs
namespace OrchestAI.Application.Queries.GetRegressionReport;

public sealed record GetRegressionReportResponse(
    Guid EvalRunId,
    Guid BaselineRunId,
    decimal CurrentPassRate,
    decimal BaselinePassRate,
    decimal PassRateDelta,
    IReadOnlyList<CaseRegressionDto> CaseDiffs
);

public sealed record CaseRegressionDto(
    Guid EvalCaseId,
    decimal CurrentScore,
    decimal? BaselineScore,
    decimal? ScoreDelta,
    bool Regressed,
    bool IsNewCase
);
```

- [ ] **Step 4: `GetRegressionReportHandler`**

```csharp
// src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportHandler.cs
using MediatR;
using Microsoft.Extensions.Logging;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetRegressionReport;

// Baseline promotion is manual per ADR-012 — EvalRun.BaselineRunId is set explicitly at
// trigger time, never auto-selected. A run with no baseline has nothing to diff against, so
// this fails loudly (ValidationException) instead of returning a response shaped like a
// report with every field zeroed out, which would be silently misleading.
public sealed class GetRegressionReportHandler : IRequestHandler<GetRegressionReportQuery, GetRegressionReportResponse>
{
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalSuiteRepository _suiteRepository;
    private readonly IEvalResultRepository _resultRepository;
    private readonly ILogger<GetRegressionReportHandler> _logger;

    public GetRegressionReportHandler(
        IEvalRunRepository runRepository,
        IEvalSuiteRepository suiteRepository,
        IEvalResultRepository resultRepository,
        ILogger<GetRegressionReportHandler> logger)
    {
        _runRepository = runRepository;
        _suiteRepository = suiteRepository;
        _resultRepository = resultRepository;
        _logger = logger;
    }

    public async Task<GetRegressionReportResponse> Handle(
        GetRegressionReportQuery request, CancellationToken cancellationToken)
    {
        var currentRun = await _runRepository.GetByIdAsync(request.EvalRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), request.EvalRunId);

        if (currentRun.BaselineRunId is not { } baselineRunId)
            throw new ValidationException(
                nameof(currentRun.BaselineRunId),
                $"Eval run {currentRun.Id} has no baseline set — a regression report needs an explicit baseline_run_id.");

        var baselineRun = await _runRepository.GetByIdAsync(baselineRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), baselineRunId);

        var suite = await _suiteRepository.GetByIdWithCasesAsync(currentRun.SuiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalSuite), currentRun.SuiteId);
        var thresholdByCaseId = suite.Cases.ToDictionary(c => c.Id, c => c.RegressionThreshold);

        var currentResults = await _resultRepository.GetByRunIdAsync(currentRun.Id, cancellationToken).ConfigureAwait(false);
        var baselineResults = await _resultRepository.GetByRunIdAsync(baselineRun.Id, cancellationToken).ConfigureAwait(false);
        var baselineByCaseId = baselineResults.ToDictionary(r => r.EvalCaseId);

        var caseDiffs = currentResults.Select(current =>
        {
            var hasBaseline = baselineByCaseId.TryGetValue(current.EvalCaseId, out var baseline);
            var scoreDelta = hasBaseline ? baseline!.Score - current.Score : (decimal?)null;
            var threshold = thresholdByCaseId.GetValueOrDefault(current.EvalCaseId, 0m);
            var regressed = hasBaseline && scoreDelta > threshold;

            return new CaseRegressionDto(
                current.EvalCaseId, current.Score, hasBaseline ? baseline!.Score : null,
                scoreDelta, regressed, IsNewCase: !hasBaseline);
        }).ToList();

        var currentPassRate = PassRate(currentResults);
        var baselinePassRate = PassRate(baselineResults);

        _logger.LogInformation(
            "Regression report for run {RunId} vs baseline {BaselineRunId}: {RegressedCount} of {CaseCount} cases regressed",
            currentRun.Id, baselineRun.Id, caseDiffs.Count(d => d.Regressed), caseDiffs.Count);

        return new GetRegressionReportResponse(
            currentRun.Id, baselineRun.Id, currentPassRate, baselinePassRate,
            currentPassRate - baselinePassRate, caseDiffs);
    }

    private static decimal PassRate(IReadOnlyList<EvalResult> results) =>
        results.Count == 0 ? 0m : (decimal)results.Count(r => r.Passed) / results.Count;
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/OrchestAI.Tests --filter GetRegressionReportHandlerTests`
Expected: PASS, all 5 tests.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/OrchestAI.Application/Queries/GetRegressionReport/ tests/OrchestAI.Tests/Application/GetRegressionReportHandlerTests.cs
git commit -m "feat: add GetRegressionReportQuery with explicit-baseline-required threshold rule"
```

---

## Task 23: `EvalsController`

**Files:**
- Create: `src/OrchestAI.API/Controllers/EvalsController.cs`

**Interfaces:**
- Consumes: every command/query from Tasks 16–22.
- Produces: the HTTP surface consumed by Task 25 (frontend).

Mirrors `TasksController`'s conventions: `[FromBody]` commands, a manual `try/catch (ValidationException)` per POST action returning `ValidationProblem`, `NotFoundException` left to propagate to the global exception-handling middleware (already wired in `Program.cs`, confirmed by `TasksController` never catching it itself). `InputPayload`/`ExpectedCriteria` are accepted as `JsonElement` in the request body (so callers post real JSON, not escaped strings) and converted with `.GetRawText()` before reaching the command.

- [ ] **Step 1: Implement**

```csharp
// src/OrchestAI.API/Controllers/EvalsController.cs
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrchestAI.Application.Commands.AddEvalCase;
using OrchestAI.Application.Commands.CreateEvalSuite;
using OrchestAI.Application.Commands.RunEvalSuite;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetEvalRunResults;
using OrchestAI.Application.Queries.GetEvalRuns;
using OrchestAI.Application.Queries.GetEvalSuites;
using OrchestAI.Application.Queries.GetRegressionReport;
using OrchestAI.Domain.Enums;

namespace OrchestAI.API.Controllers;

[ApiController]
[Route("api/v1/eval-suites")]
[Produces("application/json")]
public sealed class EvalsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<EvalsController> _logger;

    public EvalsController(IMediator mediator, ILogger<EvalsController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public sealed record AddEvalCaseRequest(
        JsonElement InputPayload, JsonElement ExpectedCriteria, EvalScorerType ScorerType,
        decimal RegressionThreshold, string Tags = "");

    public sealed record TriggerEvalRunRequest(string SubjectVersion, Guid? BaselineRunId);

    /// <summary>Creates a new eval suite targeting one agent type.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateEvalSuiteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateSuiteAsync(
        [FromBody] CreateEvalSuiteCommand command, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction(nameof(GetSuitesAsync), null, response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for CreateEvalSuite: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Lists all eval suites.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetEvalSuitesResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSuitesAsync(CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetEvalSuitesQuery(), cancellationToken);
        return Ok(response);
    }

    /// <summary>Adds a test case to an existing suite.</summary>
    [HttpPost("{suiteId:guid}/cases")]
    [ProducesResponseType(typeof(AddEvalCaseResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddCaseAsync(
        Guid suiteId, [FromBody] AddEvalCaseRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var command = new AddEvalCaseCommand(
                suiteId, request.InputPayload.GetRawText(), request.ExpectedCriteria.GetRawText(),
                request.ScorerType, request.RegressionThreshold, request.Tags);
            var response = await _mediator.Send(command, cancellationToken);
            return CreatedAtAction(nameof(GetSuitesAsync), null, response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for AddEvalCase: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Triggers a run of a suite, optionally against a baseline run for comparison.</summary>
    [HttpPost("{suiteId:guid}/runs")]
    [ProducesResponseType(typeof(RunEvalSuiteResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TriggerRunAsync(
        Guid suiteId, [FromBody] TriggerEvalRunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(
                new RunEvalSuiteCommand(suiteId, request.SubjectVersion, request.BaselineRunId), cancellationToken);
            return AcceptedAtAction(nameof(GetRunResultsAsync), new { runId = response.EvalRunId }, response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for RunEvalSuite: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Lists runs for a suite, newest first — powers the baseline-run picker.</summary>
    [HttpGet("{suiteId:guid}/runs")]
    [ProducesResponseType(typeof(GetEvalRunsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRunsAsync(Guid suiteId, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetEvalRunsQuery(suiteId), cancellationToken);
        return Ok(response);
    }

    /// <summary>Gets per-case results for one eval run.</summary>
    [HttpGet("/api/v1/eval-runs/{runId:guid}/results")]
    [ProducesResponseType(typeof(GetEvalRunResultsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRunResultsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetEvalRunResultsQuery(runId), cancellationToken);
        return Ok(response);
    }

    /// <summary>Diffs a run against its explicit baseline run — 400s if no baseline was set.</summary>
    [HttpGet("/api/v1/eval-runs/{runId:guid}/regression-report")]
    [ProducesResponseType(typeof(GetRegressionReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetRegressionReportAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new GetRegressionReportQuery(runId), cancellationToken);
            return Ok(response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for GetRegressionReport: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/OrchestAI.API/OrchestAI.API.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Manual smoke test against the running API**

Run (with the API started via `dotnet run --project src/OrchestAI.API`):
```bash
curl -s -X POST http://localhost:5000/api/v1/eval-suites \
  -H "Content-Type: application/json" \
  -d '{"name":"Research smoke","description":"basic checks","targetAgentType":"Research"}' | jq .
```
Expected: `201`, JSON body with a new `id`. (Adjust the port to whatever `dotnet run` prints on first launch.)

- [ ] **Step 4: Commit**

```bash
git add src/OrchestAI.API/Controllers/EvalsController.cs
git commit -m "feat: add EvalsController for suites, cases, runs, results, regression reports"
```

---

## Task 24: DI registration

**Files:**
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs`

**Interfaces:**
- Consumes: every repository/scorer/queue/worker/options type from Tasks 9–15, 18–19.
- Produces: a fully wired app — nothing downstream depends on this task's *interface*, but every command/query/controller from Tasks 16–23 is unusable at runtime without it.

- [ ] **Step 1: Register everything**

```csharp
// src/OrchestAI.Infrastructure/DependencyInjection.cs — add imports:
using OrchestAI.Infrastructure.Eval;

// add alongside the existing repository registrations (after IModelPricingRepository):
        services.AddScoped<IEvalSuiteRepository, EvalSuiteRepository>();
        services.AddScoped<IEvalRunRepository, EvalRunRepository>();
        services.AddScoped<IEvalResultRepository, EvalResultRepository>();

// add alongside IOrchestrationEventBus/IApprovalGateway (must be Singleton — the queue is
// written to by scoped RunEvalSuiteHandler instances and read by the singleton background
// worker, so both sides need the same instance):
        services.AddSingleton<IEvalRunQueue, InMemoryEvalRunQueue>();
        services.AddHostedService<EvalRunBackgroundWorker>();

// add alongside the other Configure<TOptions> calls:
        services.Configure<EvalOptions>(configuration.GetSection(EvalOptions.SectionName));

// add scorers (both, so IEvalScorerFactory's IEnumerable<IEvalScorer> constructor injection
// picks up all of them) and the factory, alongside the agent/tool registrations near the
// bottom of the method:
        services.AddSingleton<IEvalScorer, RuleBasedScorer>();
        services.AddSingleton<IEvalScorer, LlmJudgeScorer>();
        services.AddSingleton<IEvalScorerFactory, EvalScorerFactory>();
```

- [ ] **Step 2: Build**

Run: `dotnet build src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Verify the app starts and resolves the full DI graph**

Run: `dotnet run --project src/OrchestAI.API`
Expected: starts without a `DependencyInjection`/`InvalidOperationException` about unresolvable services (ASP.NET Core validates the scoped/singleton graph at startup in `Development`). Ctrl+C to stop.

- [ ] **Step 4: Run the full test suite one more time**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: all pass — this is the last backend task before the frontend/docs tasks, a good checkpoint to confirm nothing regressed across the whole plan so far.

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Infrastructure/DependencyInjection.cs
git commit -m "feat: wire eval repositories, scorers, queue, and background worker into DI"
```

---

## Task 25: Frontend — `EvalsPage.jsx` and nav wiring

**Files:**
- Create: `frontend/src/EvalsPage.jsx`
- Modify: `frontend/src/App.jsx`

**Interfaces:**
- Consumes: `EvalsController`'s endpoints (Task 23).
- Produces: a new "🎯 Evals" top-level view, mirroring `ObservabilityPage.jsx`'s single-file, inline-styled, `fetch`-in-`useEffect` convention (no component library, no separate API client module exists in this codebase — `ObservabilityPage.jsx` is the template).

Per the spec's explicit non-goal, there is **no** case-authoring UI here — suites/cases are seeded via `EvalsController`'s endpoints directly (curl/Postman/a script). The page only does what the spec asks for: list suites, trigger a run (baseline picked from a dropdown of that suite's prior runs, not free-text), and show a run's pass/fail summary plus its regression diff against baseline.

- [ ] **Step 1: Create `EvalsPage.jsx`**

```jsx
// frontend/src/EvalsPage.jsx
import { useState, useEffect } from 'react'

const API_BASE = `${(import.meta.env.VITE_API_URL ?? 'https://orchestai-production.up.railway.app').replace(/\/$/, '')}/api/v1`

const panelStyle = {
  background: '#1e1e2e',
  border: '1px solid #313244',
  borderRadius: 8,
  padding: '16px 18px',
}

const labelStyle = {
  fontSize: 11,
  color: '#6c7086',
  textTransform: 'uppercase',
  letterSpacing: '0.07em',
  marginBottom: 8,
}

const selectStyle = {
  padding: '6px 10px',
  borderRadius: 6,
  border: '1px solid #313244',
  background: '#181825',
  color: '#cdd6f4',
  fontSize: 12,
  outline: 'none',
}

const buttonStyle = {
  padding: '6px 14px',
  borderRadius: 6,
  border: 'none',
  background: '#89b4fa',
  color: '#11111b',
  fontSize: 12,
  fontWeight: 700,
  cursor: 'pointer',
}

function SubNav({ subView, setSubView }) {
  const tabs = [['suites', 'Suites'], ['run', 'Run'], ['results', 'Results']]
  return (
    <div style={{ display: 'flex', gap: 4, marginBottom: 20, borderBottom: '1px solid #1e1e2e', paddingBottom: 12 }}>
      {tabs.map(([key, label]) => (
        <button
          key={key}
          onClick={() => setSubView(key)}
          style={{
            background: subView === key ? '#313244' : 'transparent',
            color: subView === key ? '#cdd6f4' : '#6c7086',
            border: 'none', borderRadius: 6, padding: '6px 14px', fontSize: 12, cursor: 'pointer',
            fontWeight: subView === key ? 700 : 400,
          }}
        >
          {label}
        </button>
      ))}
    </div>
  )
}

function SuitesView({ suites, selectedSuiteId, onSelect }) {
  return (
    <div style={panelStyle}>
      <div style={labelStyle}>Eval Suites</div>
      {suites.length === 0 && (
        <p style={{ color: '#6c7086', fontSize: 13 }}>
          No suites yet — create one via <code>POST /api/v1/eval-suites</code>.
        </p>
      )}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
        {suites.map(s => (
          <div
            key={s.id}
            onClick={() => onSelect(s.id)}
            style={{
              padding: '10px 12px', borderRadius: 6, cursor: 'pointer',
              background: selectedSuiteId === s.id ? '#313244' : '#181825',
              border: selectedSuiteId === s.id ? '1px solid #89b4fa' : '1px solid #313244',
            }}
          >
            <div style={{ fontSize: 13, fontWeight: 700, color: '#cdd6f4' }}>{s.name}</div>
            <div style={{ fontSize: 11, color: '#6c7086' }}>
              {s.targetAgentType} · {s.description}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function RunView({ suites, selectedSuiteId, onRunTriggered }) {
  const [runs, setRuns] = useState([])
  const [subjectVersion, setSubjectVersion] = useState('')
  const [baselineRunId, setBaselineRunId] = useState('')
  const [error, setError] = useState(null)
  const suite = suites.find(s => s.id === selectedSuiteId)

  useEffect(() => {
    if (!selectedSuiteId) return
    fetch(`${API_BASE}/eval-suites/${selectedSuiteId}/runs`)
      .then(res => res.json())
      .then(data => setRuns(data.runs))
      .catch(() => setRuns([]))
  }, [selectedSuiteId])

  if (!selectedSuiteId) {
    return <div style={panelStyle}><p style={{ color: '#6c7086', fontSize: 13 }}>Select a suite first.</p></div>
  }

  const trigger = () => {
    setError(null)
    fetch(`${API_BASE}/eval-suites/${selectedSuiteId}/runs`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        subjectVersion,
        baselineRunId: baselineRunId || null,
      }),
    })
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? `Failed: ${res.status}`)
        return res.json()
      })
      .then(data => onRunTriggered(data.evalRunId))
      .catch(err => setError(err.message))
  }

  return (
    <div style={panelStyle}>
      <div style={labelStyle}>Trigger a run — {suite?.name}</div>
      <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap' }}>
        <div>
          <div style={labelStyle}>Subject version</div>
          <input
            value={subjectVersion}
            onChange={e => setSubjectVersion(e.target.value)}
            placeholder="git SHA or prompt version"
            style={{ ...selectStyle, width: 220 }}
          />
        </div>
        <div>
          <div style={labelStyle}>Baseline run</div>
          <select value={baselineRunId} onChange={e => setBaselineRunId(e.target.value)} style={{ ...selectStyle, width: 260 }}>
            <option value="">None (first run for this suite)</option>
            {runs.map(r => (
              <option key={r.id} value={r.id}>
                {r.subjectVersion} — {r.status} — {new Date(r.triggeredAt).toLocaleString()}
              </option>
            ))}
          </select>
        </div>
        <button onClick={trigger} disabled={!subjectVersion} style={buttonStyle}>Run suite</button>
      </div>
      {error && <p style={{ color: '#f38ba8', fontSize: 12, marginTop: 10 }}>{error}</p>}
    </div>
  )
}

function ResultsView({ suites, selectedSuiteId, selectedRunId, onSelectRun }) {
  const [runs, setRuns] = useState([])
  const [results, setResults] = useState(null)
  const [regression, setRegression] = useState(null)
  const [regressionError, setRegressionError] = useState(null)

  useEffect(() => {
    if (!selectedSuiteId) return
    fetch(`${API_BASE}/eval-suites/${selectedSuiteId}/runs`)
      .then(res => res.json())
      .then(data => setRuns(data.runs))
      .catch(() => setRuns([]))
  }, [selectedSuiteId])

  useEffect(() => {
    if (!selectedRunId) return
    setResults(null)
    setRegression(null)
    setRegressionError(null)

    fetch(`${API_BASE}/eval-runs/${selectedRunId}/results`)
      .then(res => res.json())
      .then(setResults)

    fetch(`${API_BASE}/eval-runs/${selectedRunId}/regression-report`)
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? 'No baseline set for this run')
        return res.json()
      })
      .then(setRegression)
      .catch(err => setRegressionError(err.message))
  }, [selectedRunId])

  const passCount = results ? results.results.filter(r => r.passed).length : 0

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={panelStyle}>
        <div style={labelStyle}>Run</div>
        <select value={selectedRunId ?? ''} onChange={e => onSelectRun(e.target.value)} style={{ ...selectStyle, width: '100%' }}>
          <option value="" disabled>Select a run…</option>
          {runs.map(r => (
            <option key={r.id} value={r.id}>
              {r.subjectVersion} — {r.status} — {new Date(r.triggeredAt).toLocaleString()}
            </option>
          ))}
        </select>
      </div>

      {results && (
        <div style={panelStyle}>
          <div style={labelStyle}>Pass/Fail Summary</div>
          <div style={{ fontSize: 20, fontWeight: 700, color: '#cdd6f4' }}>
            {passCount} / {results.results.length} passed
          </div>
          <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ color: '#6c7086', textAlign: 'left' }}>
                <th style={{ padding: '4px 8px' }}>Case</th>
                <th style={{ padding: '4px 8px' }}>Scorer</th>
                <th style={{ padding: '4px 8px' }}>Score</th>
                <th style={{ padding: '4px 8px' }}>Passed</th>
              </tr>
            </thead>
            <tbody>
              {results.results.map(r => (
                <tr key={r.evalCaseId} style={{ borderTop: '1px solid #313244' }}>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{r.evalCaseId.slice(0, 8)}…</td>
                  <td style={{ padding: '4px 8px', color: '#6c7086' }}>{r.scorerType}</td>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{r.score}</td>
                  <td style={{ padding: '4px 8px', color: r.passed ? '#a6e3a1' : '#f38ba8' }}>
                    {r.passed ? 'Pass' : 'Fail'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {regression && (
        <div style={panelStyle}>
          <div style={labelStyle}>Regression vs. Baseline</div>
          <div style={{ fontSize: 13, color: '#cdd6f4', marginBottom: 10 }}>
            Pass rate {(regression.currentPassRate * 100).toFixed(0)}% vs baseline{' '}
            {(regression.baselinePassRate * 100).toFixed(0)}%{' '}
            <span style={{ color: regression.passRateDelta < 0 ? '#f38ba8' : '#a6e3a1' }}>
              ({regression.passRateDelta >= 0 ? '+' : ''}{(regression.passRateDelta * 100).toFixed(0)}pp)
            </span>
          </div>
          <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: 12 }}>
            <thead>
              <tr style={{ color: '#6c7086', textAlign: 'left' }}>
                <th style={{ padding: '4px 8px' }}>Case</th>
                <th style={{ padding: '4px 8px' }}>Current</th>
                <th style={{ padding: '4px 8px' }}>Baseline</th>
                <th style={{ padding: '4px 8px' }}>Delta</th>
                <th style={{ padding: '4px 8px' }}>Status</th>
              </tr>
            </thead>
            <tbody>
              {regression.caseDiffs.map(d => (
                <tr key={d.evalCaseId} style={{ borderTop: '1px solid #313244' }}>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{d.evalCaseId.slice(0, 8)}…</td>
                  <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{d.currentScore}</td>
                  <td style={{ padding: '4px 8px', color: '#6c7086' }}>{d.baselineScore ?? '—'}</td>
                  <td style={{ padding: '4px 8px', color: '#6c7086' }}>{d.scoreDelta ?? '—'}</td>
                  <td style={{ padding: '4px 8px', color: d.regressed ? '#f38ba8' : d.isNewCase ? '#f9e2af' : '#a6e3a1' }}>
                    {d.regressed ? 'Regressed' : d.isNewCase ? 'New case' : 'OK'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {regressionError && (
        <div style={{ ...panelStyle, color: '#6c7086', fontSize: 12 }}>{regressionError}</div>
      )}
    </div>
  )
}

export default function EvalsPage() {
  const [subView, setSubView] = useState('suites')
  const [suites, setSuites] = useState([])
  const [selectedSuiteId, setSelectedSuiteId] = useState(null)
  const [selectedRunId, setSelectedRunId] = useState(null)

  useEffect(() => {
    fetch(`${API_BASE}/eval-suites`)
      .then(res => res.json())
      .then(data => setSuites(data.suites))
      .catch(() => setSuites([]))
  }, [])

  return (
    <div style={{ padding: '20px 24px', maxWidth: 1100 }}>
      <SubNav subView={subView} setSubView={setSubView} />
      {subView === 'suites' && (
        <SuitesView suites={suites} selectedSuiteId={selectedSuiteId} onSelect={setSelectedSuiteId} />
      )}
      {subView === 'run' && (
        <RunView
          suites={suites}
          selectedSuiteId={selectedSuiteId}
          onRunTriggered={runId => { setSelectedRunId(runId); setSubView('results') }}
        />
      )}
      {subView === 'results' && (
        <ResultsView
          suites={suites}
          selectedSuiteId={selectedSuiteId}
          selectedRunId={selectedRunId}
          onSelectRun={setSelectedRunId}
        />
      )}
    </div>
  )
}
```

- [ ] **Step 2: Wire the new tab into `App.jsx`**

```jsx
// frontend/src/App.jsx — add the import alongside the ObservabilityPage import:
import EvalsPage from './EvalsPage'

// add a nav button alongside the existing 📊 Observability button:
          <button
            onClick={() => setView('evals')}
            style={{
              background: view === 'evals' ? '#313244' : 'transparent',
              color: view === 'evals' ? '#cdd6f4' : '#6c7086',
              border: 'none', borderRadius: 6, padding: '5px 12px', fontSize: 12, cursor: 'pointer',
            }}
          >
            🎯 Evals
          </button>

// extend the view-switch chain right after the `view === 'observability'` branch:
      ) : view === 'evals' ? (
        <EvalsPage />
```

- [ ] **Step 3: Manual browser verification**

Run:
```bash
docker compose up -d
dotnet run --project src/OrchestAI.API &
cd frontend && npm run dev
```
Open the printed Vite URL, click "🎯 Evals". Expected: "Suites" tab loads (empty state if no suites seeded yet — seed one via the `curl` command from Task 23 Step 3, then an `AddEvalCase` call, to see a populated suite in the list). Click a suite, switch to "Run", enter a subject version, trigger a run, confirm it lands on "Results" with the pass/fail table populated once the background worker finishes (may take a few seconds for real agent calls).

- [ ] **Step 4: Commit**

```bash
git add frontend/src/EvalsPage.jsx frontend/src/App.jsx
git commit -m "feat: add Evals tab — suite list, run trigger with baseline picker, results/regression view"
```

---

## Task 26: ADR-012 — Evaluation & Scoring Data Model

**Files:**
- Modify: `DECISIONS.md`

**Interfaces:**
- Consumes: every design decision resolved in this plan's header and Tasks 1–24.
- Produces: none — documentation only.

- [ ] **Step 1: Append ADR-012, mirroring ADR-011's section format**

```markdown
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
```

- [ ] **Step 2: Commit**

```bash
git add DECISIONS.md
git commit -m "docs: add ADR-012 for the evaluation and scoring data model"
```

---

## Task 27: Update `OBSERVABILITY.md` with eval cost segregation

**Files:**
- Modify: `OBSERVABILITY.md`

**Interfaces:**
- Consumes: Task 8's `Source` filter, Task 26's ADR-012.
- Produces: none — documentation only. This is the last task in the plan.

- [ ] **Step 1: Add a new section after "## 2. Aggregate"**

```markdown
## 2a. Eval cost segregation

Week 8 added a `CostSource` discriminator (`Production`/`Eval`) to `CostLedger`, plus a nullable
`EvalRunId` on both `CostLedger` and `AgentExecution`. Every eval-case invocation the
`EvalRunBackgroundWorker` runs — and every `LlmJudgeScorer` judge call it triggers — writes its
`CostLedger` row tagged `Source=Eval`. Production traffic (`StartOrchestrationHandler`,
`ResumeOrchestrationTaskHandler`) never sets `EvalRunId`, so those rows default to
`Source=Production`.

**Where it's filtered:** `ICostLedgerRepository.GetDailyAggregatesAsync` — the single method
behind both `CostRollupBackgroundService` (the 5-minute rollup job) and `GetCostDashboardHandler`'s
live "today" branch — adds `Where(c => c.Source == CostSource.Production)`. Nothing downstream of
that method ever sees eval cost; there's no second filter to remember elsewhere. See
`CostLedgerRepositoryEvalFilterTests` for the test proving a mixed batch of production and eval
rows aggregates to production-only totals.

Full reasoning: ADR-012 in `DECISIONS.md`.
```

- [ ] **Step 2: Commit**

```bash
git add OBSERVABILITY.md
git commit -m "docs: document eval cost segregation in OBSERVABILITY.md"
```

---
