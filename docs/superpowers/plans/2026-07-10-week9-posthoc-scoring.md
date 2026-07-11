# Week 9: Post-Hoc Scoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Score existing `AgentExecution` traces after the fact — by date range, agent type, or explicit trace ID list — without re-running the agent, reusing the Week 8 `EvalResult`/cost-tagging pipeline so both live-suite and post-hoc scores feed one reporting surface.

**Architecture:** `RequestPostHocScoringCommand` resolves trace selection to a concrete, capped `AgentExecutionId` list *at request time*, creates an `EvalRun(Source=PostHoc)` carrying that resolved list + rubric, and enqueues it on the existing `IEvalRunQueue`. `EvalRunBackgroundWorker` gains a second branch (`ProcessPostHocRunAsync`) that, for each trace, checks idempotency, wraps the rubric in a transient (unpersisted) `EvalCase` so the existing `LlmJudgeScorer` can be reused unmodified, scores it, and writes an `EvalResult` with `EvalCaseId = null`. A new query reports pass rate / score distribution / skipped count per post-hoc run.

**Tech Stack:** C# .NET 8, EF Core 8 (PostgreSQL), MediatR, xUnit + FluentAssertions + Moq, React (EvalsPage.jsx).

## Global Constraints

- Post-hoc scoring never re-invokes an agent — it only reads `AgentExecution.OutputResult` from rows that already exist with `Status == Completed`.
- Post-hoc scoring is **judge-only** this week — `RuleBasedScorer` is rejected at the command handler with a `ValidationException` (see Task 4, and ADR-013 confirmation #2).
- Every post-hoc request must resolve to a **bounded, capped** trace set at request time — never lazily at worker time — enforced by `EvalOptions.MaxPostHocTracesPerRequestCeiling` (default 500) and a per-request `MaxTraces` the handler validates against the actual match count, throwing rather than silently truncating.
- Idempotency: a given `(AgentExecutionId, ScorerType, ScorerVersion)` tuple is scored at most once across *all* post-hoc runs **by default** — enforced by an application-level pre-check in the worker, backstopped by a partial unique index in Postgres. `RequestPostHocScoringCommand.ForceRescore` is the deliberate, distinct-from-default override: it **supersedes** (delete-then-insert) rather than appends, so the unique index stays a real invariant instead of needing to be relaxed.
- Judge LLM calls made during post-hoc scoring flow through the exact same `LlmJudgeScorer` → `CostLedger.Create(..., source: CostSource.Eval, evalRunId: ...)` path as Week 8 live scoring — no second cost-tagging code path.
- No regression/baseline comparison for post-hoc runs (`GetRegressionReportQuery` explicitly rejects non-`LiveSuite` runs).
- No UI for complex trace-selection queries — date range + agent type + explicit ID list + rubric + max-trace cap is the whole form.
- No scheduled/automatic post-hoc scoring — triggered on demand only.

---

## Investigation summary (already done — do not re-derive)

Read directly from the current codebase before writing this plan:

- `EvalResult.EvalCaseId` is currently `Guid` (non-nullable), `EvalResultConfiguration` has `.IsRequired()` on it and a unique index on `(EvalRunId, EvalCaseId)`. **Migration required** (confirmation #1).
- `EvalRun.SuiteId` is currently `Guid` (non-nullable). No `Source` discriminator exists. **Migration required.**
- `IEvalScorer.ScoreAsync(EvalCase evalCase, string actualOutput, EvalScoringContext context, ...)` takes a full `EvalCase`, not raw criteria. `RuleBasedScorer` requires a `{"mode": ExactMatch|Regex|JsonSchema, ...}` `ExpectedCriteria` shape tied to a predefined case; `LlmJudgeScorer` only ever reads `{"rubric": "...", "passThreshold": ...}` from `ExpectedCriteria` and otherwise doesn't touch case-specific fields. **Decision (confirmation #2): post-hoc scoring is judge-only.** Reuse `IEvalScorer` unchanged by constructing a transient, unpersisted `EvalCase` via a new `EvalCase.CreateEphemeral(rubric, passThreshold)` factory — no interface change, no churn to existing scorer tests.
- `EvalScoringContext(Guid OrchestrationTaskId, Guid EvalRunId)` — `LlmJudgeScorer` needs `OrchestrationTaskId` because `CostLedger.OrchestrationTaskId` is `NOT NULL`. A historical `AgentExecution` already has a real `OrchestrationTaskId` — reuse it directly, no new `OrchestrationTask` needed for post-hoc scoring.
- `EvalResult.AgentExecutionId` is already nullable with **no enforced FK** (ADR-012 Decision 1, written specifically to anticipate this week) — no schema change needed there.
- `CostLedger.Source`/`EvalRunId` discriminator (ADR-012 Decision 2) already segregates eval cost from production dashboards via `ICostLedgerRepository.GetDailyAggregatesAsync`'s single filter — `LlmJudgeScorer` writes through this unchanged regardless of whether the triggering `EvalRun` is `LiveSuite` or `PostHoc`.
- `EvalRunBackgroundWorker.ProcessRunAsync` currently assumes every run has a suite; it must branch on `run.Source` before touching `run.SuiteId`.
- `GetRegressionReportHandler` reads `currentRun.SuiteId` directly (non-nullable today) — once `SuiteId` becomes `Guid?`, this call site needs an explicit guard rejecting post-hoc runs (a regression report is meaningless without a suite/baseline).
- Migrations are applied automatically on startup via `dbContext.Database.MigrateAsync()` (`Program.cs:67`) — no manual `dotnet ef database update` step needed once the migration file exists.
- DI registrations for all eval services live in `src/OrchestAI.Infrastructure/DependencyInjection.cs:47-144` — no *new* service registrations are needed this week (only new methods on already-registered interfaces/implementations).
- Test conventions confirmed by reading existing eval tests: xUnit `[Fact]`, FluentAssertions, Moq for handler/worker tests, EF Core `UseInMemoryDatabase` + `PooledDbContextFactory<AppDbContext>` for repository tests. A reusable `internal static class TestUserFactory` already exists in `tests/OrchestAI.Tests/Infrastructure/CostLedgerRepositoryEvalFilterTests.cs` (same namespace `OrchestAI.Tests.Infrastructure` — do not redefine it in new files in that namespace).

---

### Task 1: Domain & EF model changes (`EvalRunSource`, nullable `EvalCaseId`/`SuiteId`, `Rubric`, ephemeral `EvalCase`) + migration

**Files:**
- Create: `src/OrchestAI.Domain/Enums/EvalRunSource.cs`
- Modify: `src/OrchestAI.Domain/Entities/EvalRun.cs`
- Modify: `src/OrchestAI.Domain/Entities/EvalResult.cs`
- Modify: `src/OrchestAI.Domain/Entities/EvalCase.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/Configurations/EvalRunConfiguration.cs`
- Modify: `src/OrchestAI.Infrastructure/Data/Configurations/EvalResultConfiguration.cs`
- Modify: `src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsResponse.cs`
- Modify: `src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportHandler.cs`
- Test: `tests/OrchestAI.Tests/Domain/EvalRunTests.cs`
- Test: `tests/OrchestAI.Tests/Domain/EvalCaseTests.cs`
- Test: Create `tests/OrchestAI.Tests/Domain/EvalResultTests.cs`
- Test: Modify `tests/OrchestAI.Tests/Application/GetRegressionReportHandlerTests.cs`
- Create (generated): `src/OrchestAI.Infrastructure/Migrations/<timestamp>_AddPostHocScoring.cs`

**Interfaces:**
- Produces: `EvalRunSource { LiveSuite, PostHoc }`; `EvalRun.CreatePostHoc(string subjectVersion, string rubric, string selectionCriteriaJson, bool forceRescore = false)`; `EvalRun.IncrementSkippedCount()`; `EvalRun.Source`, `EvalRun.SuiteId` (now `Guid?`), `EvalRun.Rubric` (`string?`), `EvalRun.SelectionCriteriaJson` (`string?`), `EvalRun.SkippedAlreadyScoredCount` (`int`), `EvalRun.ForceRescore` (`bool`); `EvalResult.EvalCaseId` (now `Guid?`), `EvalResult.Rubric` (`string?`); `EvalResult.Create(..., string? rubric = null)` (rubric appended as trailing optional param, all existing positional call sites unaffected); `EvalCase.CreateEphemeral(string rubric, decimal? passThreshold)`.

**Note on `ForceRescore`:** it's a trailing optional parameter (`= false`) on `CreatePostHoc`, not a required one — every existing 3-arg call to `CreatePostHoc` written elsewhere in this plan (Tasks 3, 5, 6, 8) keeps compiling unchanged and keeps the default skip-if-already-scored behavior. Only the new tests that explicitly exercise the override pass `forceRescore: true`.

- [ ] **Step 1: Write failing domain tests for the new `EvalRun`/`EvalCase`/`EvalResult` behavior**

Append to `tests/OrchestAI.Tests/Domain/EvalRunTests.cs`:

```csharp
    [Fact]
    public void Create_DefaultsToLiveSuiteSource()
    {
        var run = EvalRun.Create(Guid.NewGuid(), "commit-abc123", null);

        run.Source.Should().Be(EvalRunSource.LiveSuite);
        run.SuiteId.Should().NotBeNull();
    }

    [Fact]
    public void CreatePostHoc_SetsSourceRubricAndNullSuiteId()
    {
        var run = EvalRun.CreatePostHoc(
            "posthoc-20260710120000", "was the tool call appropriate?", "{\"resolvedTraceIds\":[]}");

        run.Source.Should().Be(EvalRunSource.PostHoc);
        run.SuiteId.Should().BeNull();
        run.Rubric.Should().Be("was the tool call appropriate?");
        run.SelectionCriteriaJson.Should().Be("{\"resolvedTraceIds\":[]}");
        run.Status.Should().Be(EvalRunStatus.Pending);
        run.SkippedAlreadyScoredCount.Should().Be(0);
        run.ForceRescore.Should().BeFalse("default post-hoc runs skip already-scored traces rather than superseding them");
    }

    [Fact]
    public void CreatePostHoc_ForceRescoreTrue_SetsFlagExplicitly()
    {
        var run = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{}", forceRescore: true);

        run.ForceRescore.Should().BeTrue();
    }

    [Fact]
    public void IncrementSkippedCount_IncrementsFromZero()
    {
        var run = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{}");

        run.IncrementSkippedCount();
        run.IncrementSkippedCount();

        run.SkippedAlreadyScoredCount.Should().Be(2);
    }
```

Append to `tests/OrchestAI.Tests/Domain/EvalCaseTests.cs`:

```csharp
    [Fact]
    public void CreateEphemeral_BuildsTransientLlmJudgeCaseWithRubricCriteria()
    {
        var evalCase = EvalCase.CreateEphemeral("was the tool call appropriate?", passThreshold: 0.75m);

        evalCase.ScorerType.Should().Be(EvalScorerType.LlmJudge);
        evalCase.SuiteId.Should().Be(Guid.Empty);

        using var doc = System.Text.Json.JsonDocument.Parse(evalCase.ExpectedCriteria);
        doc.RootElement.GetProperty("rubric").GetString().Should().Be("was the tool call appropriate?");
        doc.RootElement.GetProperty("passThreshold").GetDecimal().Should().Be(0.75m);
    }

    [Fact]
    public void CreateEphemeral_NoPassThreshold_OmitsPassThresholdFromCriteria()
    {
        var evalCase = EvalCase.CreateEphemeral("rubric text", passThreshold: null);

        using var doc = System.Text.Json.JsonDocument.Parse(evalCase.ExpectedCriteria);
        doc.RootElement.TryGetProperty("passThreshold", out _).Should().BeFalse();
    }
```

Create `tests/OrchestAI.Tests/Domain/EvalResultTests.cs`:

```csharp
using FluentAssertions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Tests.Domain;

public sealed class EvalResultTests
{
    [Fact]
    public void Create_NullEvalCaseIdWithRubric_PersistsPostHocShape()
    {
        var runId = Guid.NewGuid();
        var executionId = Guid.NewGuid();

        var result = EvalResult.Create(
            runId, evalCaseId: null, executionId, EvalScorerType.LlmJudge, "llm-judge-v1",
            score: 0.9m, passed: true, scorerOutput: "{}", rubric: "was the tool call appropriate?");

        result.EvalCaseId.Should().BeNull();
        result.Rubric.Should().Be("was the tool call appropriate?");
        result.AgentExecutionId.Should().Be(executionId);
    }

    [Fact]
    public void Create_LiveCaseResult_RubricDefaultsToNull()
    {
        var result = EvalResult.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), EvalScorerType.RuleBased, "rule-based-v1",
            score: 1.0m, passed: true, scorerOutput: "{}");

        result.Rubric.Should().BeNull();
        result.EvalCaseId.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~EvalRunTests|FullyQualifiedName~EvalCaseTests|FullyQualifiedName~EvalResultTests"`
Expected: FAIL — `EvalRunSource`, `CreatePostHoc`, `IncrementSkippedCount`, `CreateEphemeral`, and the `rubric` parameter don't exist yet (compile errors).

- [ ] **Step 3: Create `EvalRunSource` enum**

```csharp
namespace OrchestAI.Domain.Enums;

public enum EvalRunSource
{
    LiveSuite,
    PostHoc
}
```

- [ ] **Step 4: Update `EvalRun` entity**

Replace the full contents of `src/OrchestAI.Domain/Entities/EvalRun.cs`:

```csharp
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

public sealed class EvalRun
{
    private EvalRun() { }

    public Guid Id { get; private set; }
    public Guid? SuiteId { get; private set; }
    public EvalRunSource Source { get; private set; }
    public DateTimeOffset TriggeredAt { get; private set; }
    public EvalRunStatus Status { get; private set; }
    public Guid? BaselineRunId { get; private set; }
    public string SubjectVersion { get; private set; } = string.Empty;
    public string? Rubric { get; private set; }
    public string? SelectionCriteriaJson { get; private set; }
    public int SkippedAlreadyScoredCount { get; private set; }
    public bool ForceRescore { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public EvalSuite? Suite { get; private set; }
    public EvalRun? BaselineRun { get; private set; }

    public static EvalRun Create(Guid suiteId, string subjectVersion, Guid? baselineRunId)
    {
        return new EvalRun
        {
            Id = Guid.NewGuid(),
            SuiteId = suiteId,
            Source = EvalRunSource.LiveSuite,
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = EvalRunStatus.Pending,
            BaselineRunId = baselineRunId,
            SubjectVersion = subjectVersion
        };
    }

    // Post-hoc runs have no suite — `SelectionCriteriaJson` carries the resolved AgentExecutionId
    // list (and original filter, for audit) the background worker iterates. `Rubric` is the
    // single free-text judge rubric applied to every trace in this run — see ADR-013.
    // `forceRescore` defaults false (skip-if-already-scored); true means the worker supersedes
    // (delete-then-insert) rather than skips a previously-scored trace — see ADR-013 confirmation #3.
    public static EvalRun CreatePostHoc(
        string subjectVersion, string rubric, string selectionCriteriaJson, bool forceRescore = false)
    {
        return new EvalRun
        {
            Id = Guid.NewGuid(),
            SuiteId = null,
            Source = EvalRunSource.PostHoc,
            TriggeredAt = DateTimeOffset.UtcNow,
            Status = EvalRunStatus.Pending,
            BaselineRunId = null,
            SubjectVersion = subjectVersion,
            Rubric = rubric,
            SelectionCriteriaJson = selectionCriteriaJson,
            ForceRescore = forceRescore
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

    public void IncrementSkippedCount() => SkippedAlreadyScoredCount++;
}
```

- [ ] **Step 5: Update `EvalResult` entity**

Replace the full contents of `src/OrchestAI.Domain/Entities/EvalResult.cs`:

```csharp
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Entities;

// Immutable after creation — a score is a point-in-time record of what the scorer
// concluded when the run executed, never re-derived from current scorer logic on read.
public sealed class EvalResult
{
    private EvalResult() { }

    public Guid Id { get; private set; }
    public Guid EvalRunId { get; private set; }
    public Guid? EvalCaseId { get; private set; }
    public Guid? AgentExecutionId { get; private set; }
    public EvalScorerType ScorerType { get; private set; }
    public string ScorerVersion { get; private set; } = string.Empty;
    public decimal Score { get; private set; }
    public bool Passed { get; private set; }
    public string ScorerOutput { get; private set; } = string.Empty;
    public string? Rubric { get; private set; }
    public DateTimeOffset ScoredAt { get; private set; }

    public EvalRun Run { get; private set; } = null!;
    public EvalCase? Case { get; private set; }

    public static EvalResult Create(
        Guid evalRunId,
        Guid? evalCaseId,
        Guid? agentExecutionId,
        EvalScorerType scorerType,
        string scorerVersion,
        decimal score,
        bool passed,
        string scorerOutput,
        string? rubric = null)
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
            Rubric = rubric,
            ScoredAt = DateTimeOffset.UtcNow
        };
    }
}
```

- [ ] **Step 6: Add `EvalCase.CreateEphemeral`**

In `src/OrchestAI.Domain/Entities/EvalCase.cs`, add `using System.Text.Json;` at the top and add this method inside the class, after `Create`:

```csharp
    // Builds a transient, never-persisted EvalCase so post-hoc scoring can reuse IEvalScorer
    // unchanged when there's no predefined EvalCase to point to — see ADR-013 confirmation #2.
    // Always LlmJudge: RuleBasedScorer requires machine-checkable ExpectedCriteria tied to a
    // specific predefined case, which doesn't exist for arbitrary historical traces.
    public static EvalCase CreateEphemeral(string rubric, decimal? passThreshold)
    {
        var criteria = passThreshold.HasValue
            ? JsonSerializer.Serialize(new { rubric, passThreshold = passThreshold.Value })
            : JsonSerializer.Serialize(new { rubric });

        return new EvalCase
        {
            Id = Guid.Empty,
            SuiteId = Guid.Empty,
            InputPayload = string.Empty,
            ExpectedCriteria = criteria,
            ScorerType = EvalScorerType.LlmJudge,
            RegressionThreshold = 0m,
            Tags = "posthoc-ephemeral",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
```

- [ ] **Step 7: Update `EvalRunConfiguration`**

In `src/OrchestAI.Infrastructure/Data/Configurations/EvalRunConfiguration.cs`, replace the `SuiteId` property block and the `HasOne(r => r.Suite)` block, and add the new property configurations. The full `Configure` method body becomes:

```csharp
    public void Configure(EntityTypeBuilder<EvalRun> builder)
    {
        builder.ToTable("EvalRuns");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnType("uuid")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.SuiteId)
            .HasColumnType("uuid");

        builder.Property(r => r.Source)
            .IsRequired()
            .HasMaxLength(50)
            .HasDefaultValue(EvalRunSource.LiveSuite)
            .HasConversion<string>();

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

        builder.Property(r => r.Rubric)
            .HasColumnType("text");

        builder.Property(r => r.SelectionCriteriaJson)
            .HasColumnType("jsonb");

        builder.Property(r => r.SkippedAlreadyScoredCount)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(r => r.ForceRescore)
            .IsRequired()
            .HasDefaultValue(false);

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
        builder.HasIndex(r => r.Source);
    }
```

Add `using OrchestAI.Domain.Enums;` if not already present (it already imports it for `EvalRunStatus`).

- [ ] **Step 8: Update `EvalResultConfiguration`**

Replace the `EvalCaseId` property block, add the `Rubric` property, and add the idempotency index. Full `Configure` method body:

```csharp
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

        builder.Property(r => r.Rubric)
            .HasColumnType("text");

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

        // Idempotency backstop for post-hoc scoring (Week 9) — a given (trace, scorer, version)
        // is scored at most once across all post-hoc runs. Only applies to post-hoc-origin rows
        // (EvalCaseId IS NULL); live case-based results never collide here. See ADR-013.
        builder.HasIndex(r => new { r.AgentExecutionId, r.ScorerType, r.ScorerVersion })
            .IsUnique()
            .HasFilter("\"EvalCaseId\" IS NULL AND \"AgentExecutionId\" IS NOT NULL");
    }
```

- [ ] **Step 9: Fix the `GetEvalRunResultsResponse` DTO type**

In `src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsResponse.cs`, change:

```csharp
public sealed record EvalResultDto(
    Guid EvalCaseId, Guid? AgentExecutionId, string ScorerType, string ScorerVersion,
    decimal Score, bool Passed, string ScorerOutputJson, DateTimeOffset ScoredAt);
```

to:

```csharp
public sealed record EvalResultDto(
    Guid? EvalCaseId, Guid? AgentExecutionId, string ScorerType, string ScorerVersion,
    decimal Score, bool Passed, string ScorerOutputJson, DateTimeOffset ScoredAt);
```

`GetEvalRunResultsHandler.cs` needs no change — `r.EvalCaseId` is now `Guid?` and passes straight through.

- [ ] **Step 10: Guard `GetRegressionReportHandler` against post-hoc runs**

In `src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportHandler.cs`, add `using OrchestAI.Domain.Enums;` to the usings, then change:

```csharp
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
```

to:

```csharp
        var currentRun = await _runRepository.GetByIdAsync(request.EvalRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), request.EvalRunId);

        if (currentRun.Source != EvalRunSource.LiveSuite || currentRun.SuiteId is not { } suiteId)
            throw new ValidationException(
                nameof(currentRun.Source),
                $"Eval run {currentRun.Id} is a post-hoc run and has no suite — regression reports " +
                "only apply to live-suite runs.");

        if (currentRun.BaselineRunId is not { } baselineRunId)
            throw new ValidationException(
                nameof(currentRun.BaselineRunId),
                $"Eval run {currentRun.Id} has no baseline set — a regression report needs an explicit baseline_run_id.");

        var baselineRun = await _runRepository.GetByIdAsync(baselineRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), baselineRunId);

        var suite = await _suiteRepository.GetByIdWithCasesAsync(suiteId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalSuite), suiteId);
```

Add this test to `tests/OrchestAI.Tests/Application/GetRegressionReportHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_PostHocRun_ThrowsValidation_RegressionOnlyAppliesToLiveSuiteRuns()
    {
        var postHocRun = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{}");

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(postHocRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(postHocRun);

        var handler = new GetRegressionReportHandler(
            runRepoMock.Object, Mock.Of<IEvalSuiteRepository>(), Mock.Of<IEvalResultRepository>(),
            NullLogger<GetRegressionReportHandler>.Instance);

        var act = async () => await handler.Handle(new GetRegressionReportQuery(postHocRun.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }
```

(Add `using Microsoft.Extensions.Logging.Abstractions;` if not already present in that test file — it already is, per the existing file header.)

- [ ] **Step 11: Run all Task 1 tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~EvalRunTests|FullyQualifiedName~EvalCaseTests|FullyQualifiedName~EvalResultTests|FullyQualifiedName~GetRegressionReportHandlerTests"`
Expected: PASS, all tests green.

- [ ] **Step 12: Generate and review the migration**

Run: `dotnet ef migrations add AddPostHocScoring --project src/OrchestAI.Infrastructure --startup-project src/OrchestAI.API`

Open the generated `src/OrchestAI.Infrastructure/Migrations/<timestamp>_AddPostHocScoring.cs` and confirm it contains, at minimum:
- `AlterColumn` making `EvalRuns.SuiteId` nullable
- `AddColumn` for `EvalRuns.Source` (varchar(50), default `'LiveSuite'`)
- `AddColumn` for `EvalRuns.Rubric` (text, nullable)
- `AddColumn` for `EvalRuns.SelectionCriteriaJson` (jsonb, nullable)
- `AddColumn` for `EvalRuns.SkippedAlreadyScoredCount` (int, default 0)
- `AddColumn` for `EvalRuns.ForceRescore` (bool, default false)
- `CreateIndex` on `EvalRuns.Source`
- `AlterColumn` making `EvalResults.EvalCaseId` nullable
- `AddColumn` for `EvalResults.Rubric` (text, nullable)
- `CreateIndex` unique partial index on `EvalResults (AgentExecutionId, ScorerType, ScorerVersion)` filtered `"EvalCaseId" IS NULL AND "AgentExecutionId" IS NOT NULL`

If any are missing, do not hand-patch the migration — fix the entity/configuration and regenerate.

- [ ] **Step 13: Build the whole solution to catch any remaining call-site breaks**

Run: `dotnet build OrchestAI.sln`
Expected: Build succeeds with 0 errors. (This will surface any other place in the codebase that read `EvalRun.SuiteId` or `EvalResult.EvalCaseId` as non-nullable that wasn't caught above.)

- [ ] **Step 14: Commit**

```bash
git add src/OrchestAI.Domain/Enums/EvalRunSource.cs src/OrchestAI.Domain/Entities/EvalRun.cs \
  src/OrchestAI.Domain/Entities/EvalResult.cs src/OrchestAI.Domain/Entities/EvalCase.cs \
  src/OrchestAI.Infrastructure/Data/Configurations/EvalRunConfiguration.cs \
  src/OrchestAI.Infrastructure/Data/Configurations/EvalResultConfiguration.cs \
  src/OrchestAI.Application/Queries/GetEvalRunResults/GetEvalRunResultsResponse.cs \
  src/OrchestAI.Application/Queries/GetRegressionReport/GetRegressionReportHandler.cs \
  src/OrchestAI.Infrastructure/Migrations/ \
  tests/OrchestAI.Tests/Domain/EvalRunTests.cs tests/OrchestAI.Tests/Domain/EvalCaseTests.cs \
  tests/OrchestAI.Tests/Domain/EvalResultTests.cs \
  tests/OrchestAI.Tests/Application/GetRegressionReportHandlerTests.cs
git commit -m "feat: add EvalRun.Source discriminator and nullable EvalCaseId for post-hoc scoring"
```

---

### Task 2: Trace-selection repository method

**Files:**
- Create: `src/OrchestAI.Domain/Models/TraceSelectionResult.cs`
- Modify: `src/OrchestAI.Domain/Interfaces/IAgentExecutionRepository.cs`
- Modify: `src/OrchestAI.Infrastructure/Repositories/AgentExecutionRepository.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/AgentExecutionRepositoryPostHocSelectionTests.cs`

**Interfaces:**
- Consumes: `AgentExecution.Status` (`ExecutionStatus.Completed`), `AgentExecution.CreatedAt`, `AgentExecution.AgentType`, `AgentExecution.Id`.
- Produces: `TraceSelectionResult(IReadOnlyList<Guid> AgentExecutionIds, int TotalMatched)`; `IAgentExecutionRepository.SelectForPostHocScoringAsync(DateTimeOffset? from, DateTimeOffset? to, AgentType? agentType, IReadOnlyCollection<Guid>? explicitTraceIds, int limit, CancellationToken)`.

- [ ] **Step 1: Write the failing repository test**

Create `tests/OrchestAI.Tests/Infrastructure/AgentExecutionRepositoryPostHocSelectionTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;

namespace OrchestAI.Tests.Infrastructure;

public sealed class AgentExecutionRepositoryPostHocSelectionTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    [Fact]
    public async Task SelectForPostHocScoringAsync_FiltersByDateRangeAgentTypeAndCompletedStatus()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var user = TestUserFactory.Create("posthoc-select@test.local");
        var task = OrchestrationTask.Create(user.Id, "t", "prompt");

        var inRangeResearch = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        inRangeResearch.Start();
        inRangeResearch.Complete("output", 10, 5, 0.01m);

        var inRangeCode = AgentExecution.Create(task.Id, AgentType.Code, "prompt");
        inRangeCode.Start();
        inRangeCode.Complete("output", 10, 5, 0.01m);

        var stillRunning = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        stillRunning.Start();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(inRangeResearch, inRangeCode, stillRunning);
            await seedCtx.SaveChangesAsync();
        }

        var repository = new AgentExecutionRepository(factory);
        var result = await repository.SelectForPostHocScoringAsync(
            from: DateTimeOffset.UtcNow.AddDays(-1), to: DateTimeOffset.UtcNow.AddDays(1),
            agentType: AgentType.Research, explicitTraceIds: null, limit: 10,
            cancellationToken: CancellationToken.None);

        result.TotalMatched.Should().Be(1);
        result.AgentExecutionIds.Should().ContainSingle().Which.Should().Be(inRangeResearch.Id);
    }

    [Fact]
    public async Task SelectForPostHocScoringAsync_TotalMatchedExceedsLimit_ReturnsFullCountWithTruncatedIds()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var user = TestUserFactory.Create("posthoc-cap@test.local");
        var task = OrchestrationTask.Create(user.Id, "t", "prompt");

        var executions = Enumerable.Range(0, 5).Select(_ =>
        {
            var e = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
            e.Start();
            e.Complete("output", 10, 5, 0.01m);
            return e;
        }).ToList();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(executions);
            await seedCtx.SaveChangesAsync();
        }

        var repository = new AgentExecutionRepository(factory);
        var result = await repository.SelectForPostHocScoringAsync(
            from: DateTimeOffset.UtcNow.AddDays(-1), to: DateTimeOffset.UtcNow.AddDays(1),
            agentType: null, explicitTraceIds: null, limit: 3, cancellationToken: CancellationToken.None);

        result.TotalMatched.Should().Be(5);
        result.AgentExecutionIds.Should().HaveCount(3);
    }

    [Fact]
    public async Task SelectForPostHocScoringAsync_ExplicitTraceIds_IgnoresDateRangeAndUsesIdsOnly()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var user = TestUserFactory.Create("posthoc-explicit@test.local");
        var task = OrchestrationTask.Create(user.Id, "t", "prompt");

        var wanted = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        wanted.Start();
        wanted.Complete("output", 10, 5, 0.01m);

        var notWanted = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        notWanted.Start();
        notWanted.Complete("output", 10, 5, 0.01m);

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(wanted, notWanted);
            await seedCtx.SaveChangesAsync();
        }

        var repository = new AgentExecutionRepository(factory);
        var result = await repository.SelectForPostHocScoringAsync(
            from: null, to: null, agentType: null, explicitTraceIds: [wanted.Id], limit: 10,
            cancellationToken: CancellationToken.None);

        result.TotalMatched.Should().Be(1);
        result.AgentExecutionIds.Should().ContainSingle().Which.Should().Be(wanted.Id);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~AgentExecutionRepositoryPostHocSelectionTests"`
Expected: FAIL — `SelectForPostHocScoringAsync`/`TraceSelectionResult` don't exist (compile error).

- [ ] **Step 3: Create `TraceSelectionResult`**

```csharp
namespace OrchestAI.Domain.Models;

public sealed record TraceSelectionResult(IReadOnlyList<Guid> AgentExecutionIds, int TotalMatched);
```

- [ ] **Step 4: Add the method to `IAgentExecutionRepository`**

Add to `src/OrchestAI.Domain/Interfaces/IAgentExecutionRepository.cs` (add `using OrchestAI.Domain.Enums;` for `AgentType`):

```csharp
    // Resolves which AgentExecution rows match post-hoc trace-selection criteria (Week 9). Only
    // Completed executions are eligible — a Pending/Running/Failed execution has no OutputResult
    // to score. When explicitTraceIds is non-empty it is used exclusively (date range ignored);
    // otherwise from/to/agentType filter. TotalMatched is the full match count before `limit` is
    // applied, so callers can detect and reject an over-cap selection instead of silently
    // truncating it.
    Task<TraceSelectionResult> SelectForPostHocScoringAsync(
        DateTimeOffset? from, DateTimeOffset? to, AgentType? agentType,
        IReadOnlyCollection<Guid>? explicitTraceIds, int limit, CancellationToken cancellationToken = default);
```

- [ ] **Step 5: Implement it in `AgentExecutionRepository`**

Add to `src/OrchestAI.Infrastructure/Repositories/AgentExecutionRepository.cs` (add `using OrchestAI.Domain.Enums;`):

```csharp
    public async Task<TraceSelectionResult> SelectForPostHocScoringAsync(
        DateTimeOffset? from, DateTimeOffset? to, AgentType? agentType,
        IReadOnlyCollection<Guid>? explicitTraceIds, int limit, CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        var query = ctx.AgentExecutions.Where(e => e.Status == ExecutionStatus.Completed);

        if (explicitTraceIds is { Count: > 0 })
        {
            query = query.Where(e => explicitTraceIds.Contains(e.Id));
        }
        else
        {
            if (from.HasValue) query = query.Where(e => e.CreatedAt >= from.Value);
            if (to.HasValue) query = query.Where(e => e.CreatedAt <= to.Value);
        }

        if (agentType.HasValue)
            query = query.Where(e => e.AgentType == agentType.Value);

        var totalMatched = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var ids = await query
            .OrderBy(e => e.CreatedAt)
            .Take(limit)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new TraceSelectionResult(ids, totalMatched);
    }
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~AgentExecutionRepositoryPostHocSelectionTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/OrchestAI.Domain/Models/TraceSelectionResult.cs \
  src/OrchestAI.Domain/Interfaces/IAgentExecutionRepository.cs \
  src/OrchestAI.Infrastructure/Repositories/AgentExecutionRepository.cs \
  tests/OrchestAI.Tests/Infrastructure/AgentExecutionRepositoryPostHocSelectionTests.cs
git commit -m "feat: add AgentExecutionRepository.SelectForPostHocScoringAsync trace selection"
```

---

### Task 3: Idempotency query and supersede-delete on `IEvalResultRepository`

**Files:**
- Modify: `src/OrchestAI.Domain/Interfaces/IEvalResultRepository.cs`
- Modify: `src/OrchestAI.Infrastructure/Repositories/EvalResultRepository.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/EvalResultRepositoryIdempotencyTests.cs`

**Interfaces:**
- Consumes: `EvalResult.EvalCaseId`, `EvalResult.AgentExecutionId`, `EvalResult.ScorerType`, `EvalResult.ScorerVersion`.
- Produces: `IEvalResultRepository.GetScoredAgentExecutionIdsAsync(IReadOnlyCollection<Guid> agentExecutionIds, EvalScorerType scorerType, string scorerVersion, CancellationToken)`; `IEvalResultRepository.DeletePostHocResultAsync(Guid agentExecutionId, EvalScorerType scorerType, string scorerVersion, CancellationToken)` — the supersede primitive `ForceRescore` uses (Task 5): removes any prior post-hoc result for that exact (trace, scorer, version) tuple so the worker can insert a fresh one without violating the partial unique index from Task 1. No-op if nothing existed.

- [ ] **Step 1: Write the failing test**

Create `tests/OrchestAI.Tests/Infrastructure/EvalResultRepositoryIdempotencyTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Repositories;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalResultRepositoryIdempotencyTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    [Fact]
    public async Task GetScoredAgentExecutionIdsAsync_ReturnsOnlyMatchesForExactScorerAndVersion()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var runId = Guid.NewGuid();
        var scoredExecutionId = Guid.NewGuid();
        var unscoredExecutionId = Guid.NewGuid();
        var differentVersionExecutionId = Guid.NewGuid();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.EvalResults.AddRange(
                EvalResult.Create(runId, null, scoredExecutionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"),
                EvalResult.Create(runId, null, differentVersionExecutionId, EvalScorerType.LlmJudge, "llm-judge-v0", 0.9m, true, "{}"));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new EvalResultRepository(factory);
        var scored = await repository.GetScoredAgentExecutionIdsAsync(
            [scoredExecutionId, unscoredExecutionId, differentVersionExecutionId],
            EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        scored.Should().ContainSingle().Which.Should().Be(scoredExecutionId);
    }

    [Fact]
    public async Task GetScoredAgentExecutionIdsAsync_LiveCaseBasedResult_NeverCounted()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var executionId = Guid.NewGuid();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            // A live-suite result for the same execution/scorer/version, but with a real EvalCaseId —
            // must not be mistaken for a prior post-hoc score of this trace.
            seedCtx.EvalResults.Add(
                EvalResult.Create(Guid.NewGuid(), Guid.NewGuid(), executionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new EvalResultRepository(factory);
        var scored = await repository.GetScoredAgentExecutionIdsAsync(
            [executionId], EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        scored.Should().BeEmpty();
    }

    [Fact]
    public async Task DeletePostHocResultAsync_PriorResultExists_RemovesItAndAllowsReinsert()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var executionId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.EvalResults.Add(
                EvalResult.Create(runId, null, executionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.3m, false, "{}"));
            await seedCtx.SaveChangesAsync();
        }

        var repository = new EvalResultRepository(factory);
        await repository.DeletePostHocResultAsync(executionId, EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        var stillScored = await repository.GetScoredAgentExecutionIdsAsync(
            [executionId], EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);
        stillScored.Should().BeEmpty("the prior post-hoc result was deleted so re-scoring the same trace can proceed");

        // Re-insert (simulating the supersede: delete-then-add) must not violate the partial unique index.
        await repository.AddAsync(
            EvalResult.Create(runId, null, executionId, EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"),
            CancellationToken.None);

        var results = await repository.GetByRunIdAsync(runId, CancellationToken.None);
        results.Should().ContainSingle().Which.Score.Should().Be(0.9m);
    }

    [Fact]
    public async Task DeletePostHocResultAsync_NoPriorResult_IsNoOp()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);
        var repository = new EvalResultRepository(factory);

        var act = async () => await repository.DeletePostHocResultAsync(
            Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~EvalResultRepositoryIdempotencyTests"`
Expected: FAIL — methods don't exist (compile error).

- [ ] **Step 3: Add the methods to `IEvalResultRepository`**

```csharp
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Domain.Interfaces;

public interface IEvalResultRepository
{
    Task<IReadOnlyList<EvalResult>> GetByRunIdAsync(Guid evalRunId, CancellationToken cancellationToken = default);
    Task AddAsync(EvalResult result, CancellationToken cancellationToken = default);

    // Idempotency check for post-hoc scoring (Week 9). Returns the subset of `agentExecutionIds`
    // already scored by this exact (scorer, version) via a prior post-hoc result. EvalCaseId IS
    // NULL marks a post-hoc-origin row — a live case-based result never collides here because it
    // always carries a real EvalCaseId.
    Task<IReadOnlyList<Guid>> GetScoredAgentExecutionIdsAsync(
        IReadOnlyCollection<Guid> agentExecutionIds, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default);

    // Supersede primitive for ForceRescore (Week 9) — deletes any prior post-hoc result for this
    // exact (trace, scorer, version) tuple so the worker can insert a fresh one without violating
    // the partial unique index. No-op if nothing existed (covers first-time scoring under
    // ForceRescore, which never checked existence in the first place). See ADR-013 confirmation #3.
    Task DeletePostHocResultAsync(
        Guid agentExecutionId, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement them in `EvalResultRepository`**

Add to `src/OrchestAI.Infrastructure/Repositories/EvalResultRepository.cs` (add `using OrchestAI.Domain.Enums;`):

```csharp
    public async Task<IReadOnlyList<Guid>> GetScoredAgentExecutionIdsAsync(
        IReadOnlyCollection<Guid> agentExecutionIds, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await ctx.EvalResults
            .Where(r => r.EvalCaseId == null
                && r.AgentExecutionId != null
                && agentExecutionIds.Contains(r.AgentExecutionId!.Value)
                && r.ScorerType == scorerType
                && r.ScorerVersion == scorerVersion)
            .Select(r => r.AgentExecutionId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DeletePostHocResultAsync(
        Guid agentExecutionId, EvalScorerType scorerType, string scorerVersion,
        CancellationToken cancellationToken = default)
    {
        await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var existing = await ctx.EvalResults
            .Where(r => r.EvalCaseId == null
                && r.AgentExecutionId == agentExecutionId
                && r.ScorerType == scorerType
                && r.ScorerVersion == scorerVersion)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existing.Count == 0) return;

        ctx.EvalResults.RemoveRange(existing);
        await ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~EvalResultRepositoryIdempotencyTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Domain/Interfaces/IEvalResultRepository.cs \
  src/OrchestAI.Infrastructure/Repositories/EvalResultRepository.cs \
  tests/OrchestAI.Tests/Infrastructure/EvalResultRepositoryIdempotencyTests.cs
git commit -m "feat: add EvalResultRepository idempotency lookup and supersede-delete for post-hoc scoring"
```

---

### Task 4: `RequestPostHocScoringCommand` (trigger + validation)

**Files:**
- Move: `src/OrchestAI.Infrastructure/Configuration/EvalOptions.cs` → `src/OrchestAI.Application/Configuration/EvalOptions.cs` (namespace `OrchestAI.Infrastructure.Configuration` → `OrchestAI.Application.Configuration`) — see architecture note below
- Modify: `src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj` (add `<ProjectReference Include="..\OrchestAI.Application\OrchestAI.Application.csproj" />`)
- Modify: `src/OrchestAI.Infrastructure/Eval/LlmJudgeScorer.cs` (using statement only: `OrchestAI.Infrastructure.Configuration` → `OrchestAI.Application.Configuration`)
- Modify: `src/OrchestAI.Infrastructure/DependencyInjection.cs` (using statement only, same change)
- Create: `src/OrchestAI.Application/Commands/RequestPostHocScoring/RequestPostHocScoringCommand.cs`
- Create: `src/OrchestAI.Application/Commands/RequestPostHocScoring/RequestPostHocScoringResponse.cs`
- Create: `src/OrchestAI.Application/Commands/RequestPostHocScoring/RequestPostHocScoringHandler.cs`
- Test: Create `tests/OrchestAI.Tests/Application/RequestPostHocScoringHandlerTests.cs`

**Architecture note (discovered during Task 4 implementation, corrected in-plan):** this codebase's actual project reference graph is `Application → Domain`, `Infrastructure → Domain` (siblings, neither references the other), `API → Application + Infrastructure`. The original plan draft put `RequestPostHocScoringHandler` (in `OrchestAI.Application`) behind `IOptions<EvalOptions>` from `OrchestAI.Infrastructure.Configuration` — an Application→Infrastructure dependency, which inverts Clean Architecture layering and is exactly what this project's CLAUDE.md forbids ("never concrete dependencies" across layers). The fix: relocate `EvalOptions` itself to `OrchestAI.Application.Configuration` and add a new `Infrastructure → Application` project reference so `LlmJudgeScorer`/`DependencyInjection.cs` (both in Infrastructure) can still reach it. This is a safe, non-cyclic, conventional addition (`Domain ← Application ← Infrastructure ← API` remains a valid DAG) — the reverse (Application → Infrastructure) would not have been.

**Interfaces:**
- Consumes: `IAgentExecutionRepository.SelectForPostHocScoringAsync` (Task 2), `IEvalRunRepository.AddAsync`, `IEvalRunQueue.EnqueueAsync`, `EvalRun.CreatePostHoc` (Task 1), `EvalOptions.MaxPostHocTracesPerRequestCeiling` (now in `OrchestAI.Application.Configuration`).
- Produces: `RequestPostHocScoringCommand(DateTimeOffset? DateFrom, DateTimeOffset? DateTo, AgentType? AgentType, IReadOnlyList<Guid>? TraceIds, EvalScorerType ScorerType, string Rubric, decimal? PassThreshold, int MaxTraces, bool ForceRescore = false) : IRequest<RequestPostHocScoringResponse>`; `RequestPostHocScoringResponse(Guid EvalRunId, string Status, int ResolvedTraceCount, DateTimeOffset TriggeredAt)`.

**Note on `ForceRescore`:** trailing optional (`= false`), so every existing test below that constructs a `RequestPostHocScoringCommand` without it keeps compiling and keeps default (skip-if-already-scored) behavior.

- [ ] **Step 1: Write the failing handler tests**

Create `tests/OrchestAI.Tests/Application/RequestPostHocScoringHandlerTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Application.Configuration;

namespace OrchestAI.Tests.Application;

public sealed class RequestPostHocScoringHandlerTests
{
    private static RequestPostHocScoringHandler BuildHandler(
        IAgentExecutionRepository executionRepo, IEvalRunRepository runRepo, IEvalRunQueue queue)
    {
        var options = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });
        return new RequestPostHocScoringHandler(
            executionRepo, runRepo, queue, options, NullLogger<RequestPostHocScoringHandler>.Instance);
    }

    [Fact]
    public async Task Handle_NoDateRangeAndNoTraceIds_ThrowsValidation_AgentTypeAloneDoesNotBoundSelection()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateFrom: null, DateTo: null, AgentType: AgentType.Research, TraceIds: null,
            ScorerType: EvalScorerType.LlmJudge, Rubric: "was it appropriate?", PassThreshold: null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_RuleBasedScorerType_ThrowsValidation_PostHocIsJudgeOnly()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateFrom: DateTimeOffset.UtcNow.AddDays(-7), DateTo: DateTimeOffset.UtcNow, AgentType: null,
            TraceIds: null, ScorerType: EvalScorerType.RuleBased, Rubric: "n/a", PassThreshold: null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_EmptyRubric_ThrowsValidation()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateFrom: DateTimeOffset.UtcNow.AddDays(-7), DateTo: DateTimeOffset.UtcNow, AgentType: null,
            TraceIds: null, ScorerType: EvalScorerType.LlmJudge, Rubric: "   ", PassThreshold: null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_MaxTracesExceedsCeiling_ThrowsValidation()
    {
        var handler = BuildHandler(Mock.Of<IAgentExecutionRepository>(), Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "rubric", null, MaxTraces: 5000);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_SelectionExceedsMaxTraces_ThrowsValidationInsteadOfSilentlyTruncating()
    {
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult([Guid.NewGuid(), Guid.NewGuid()], TotalMatched: 250));

        var handler = BuildHandler(executionRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "was it appropriate?", null, MaxTraces: 2);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_NoMatchingTraces_ThrowsValidation()
    {
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult([], TotalMatched: 0));

        var handler = BuildHandler(executionRepoMock.Object, Mock.Of<IEvalRunRepository>(), Mock.Of<IEvalRunQueue>());

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "was it appropriate?", null, MaxTraces: 100);

        var act = async () => await handler.Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_ValidBoundedSelection_CreatesPostHocRunAndEnqueues()
    {
        var resolvedIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult(resolvedIds, TotalMatched: 2));

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = BuildHandler(executionRepoMock.Object, runRepoMock.Object, queueMock.Object);

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, AgentType.Research, null,
            EvalScorerType.LlmJudge, "was it appropriate?", 0.7m, MaxTraces: 100);

        var response = await handler.Handle(command, CancellationToken.None);

        response.ResolvedTraceCount.Should().Be(2);
        response.Status.Should().Be("Pending");
        runRepoMock.Verify(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()), Times.Once);
        queueMock.Verify(q => q.EnqueueAsync(response.EvalRunId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ForceRescoreTrue_ThreadsFlagOntoCreatedRun()
    {
        var resolvedIds = new List<Guid> { Guid.NewGuid() };
        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock
            .Setup(r => r.SelectForPostHocScoringAsync(
                It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<AgentType?>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TraceSelectionResult(resolvedIds, TotalMatched: 1));

        EvalRun? captured = null;
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>()))
            .Callback<EvalRun, CancellationToken>((r, _) => captured = r)
            .Returns(Task.CompletedTask);

        var queueMock = new Mock<IEvalRunQueue>();
        queueMock.Setup(q => q.EnqueueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var handler = BuildHandler(executionRepoMock.Object, runRepoMock.Object, queueMock.Object);

        var command = new RequestPostHocScoringCommand(
            DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, null, null,
            EvalScorerType.LlmJudge, "was it appropriate?", null, MaxTraces: 100, ForceRescore: true);

        await handler.Handle(command, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ForceRescore.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~RequestPostHocScoringHandlerTests"`
Expected: FAIL — types don't exist yet (compile error).

- [ ] **Step 3: Move `EvalOptions` to `OrchestAI.Application.Configuration` and add the hard ceiling**

Per the architecture note above, `RequestPostHocScoringHandler` (Application layer) cannot depend on `OrchestAI.Infrastructure.Configuration`. Move the file:

`git mv src/OrchestAI.Infrastructure/Configuration/EvalOptions.cs src/OrchestAI.Application/Configuration/EvalOptions.cs`

Change its namespace from `OrchestAI.Infrastructure.Configuration` to `OrchestAI.Application.Configuration`, and add the new property inside the class:

```csharp
    // Hard ceiling on RequestPostHocScoringCommand.MaxTraces — a caller-supplied cap smaller than
    // this is fine, but a single post-hoc request can never ask for more than this many judge
    // calls, regardless of what the caller passes. See ADR-013 confirmation #4.
    public int MaxPostHocTracesPerRequestCeiling { get; init; } = 500;
```

Add a project reference so Infrastructure can still reach it — in `src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj`, inside the existing `<ItemGroup>` that has the `OrchestAI.Domain` `ProjectReference`, add:

```xml
    <ProjectReference Include="..\OrchestAI.Application\OrchestAI.Application.csproj" />
```

Update the two existing Infrastructure consumers' using statements (`OrchestAI.Infrastructure.Configuration` → `OrchestAI.Application.Configuration`, no other changes):
- `src/OrchestAI.Infrastructure/Eval/LlmJudgeScorer.cs` (its `using OrchestAI.Infrastructure.Configuration;` line)
- `src/OrchestAI.Infrastructure/DependencyInjection.cs` (its `using OrchestAI.Infrastructure.Configuration;` line)

Run `dotnet build OrchestAI.sln` after this step specifically to confirm both existing consumers still compile before continuing to the command/handler below.

- [ ] **Step 4: Create `RequestPostHocScoringCommand`**

```csharp
using MediatR;
using OrchestAI.Domain.Enums;

namespace OrchestAI.Application.Commands.RequestPostHocScoring;

public sealed record RequestPostHocScoringCommand(
    DateTimeOffset? DateFrom,
    DateTimeOffset? DateTo,
    AgentType? AgentType,
    IReadOnlyList<Guid>? TraceIds,
    EvalScorerType ScorerType,
    string Rubric,
    decimal? PassThreshold,
    int MaxTraces,
    bool ForceRescore = false) : IRequest<RequestPostHocScoringResponse>;
```

- [ ] **Step 5: Create `RequestPostHocScoringResponse`**

```csharp
namespace OrchestAI.Application.Commands.RequestPostHocScoring;

public sealed record RequestPostHocScoringResponse(
    Guid EvalRunId, string Status, int ResolvedTraceCount, DateTimeOffset TriggeredAt);
```

- [ ] **Step 6: Create `RequestPostHocScoringHandler`**

```csharp
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Application.Configuration;

namespace OrchestAI.Application.Commands.RequestPostHocScoring;

// Resolves trace selection to a concrete AgentExecutionId list at request time (not lazily by
// the worker) — see ADR-013 Decision 3. This makes the MaxTraces cap enforcement a single choke
// point and makes a given EvalRunId's scope reproducible regardless of what production traffic
// arrives after the request is enqueued.
public sealed class RequestPostHocScoringHandler
    : IRequestHandler<RequestPostHocScoringCommand, RequestPostHocScoringResponse>
{
    private readonly IAgentExecutionRepository _executionRepository;
    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalRunQueue _queue;
    private readonly IOptions<EvalOptions> _options;
    private readonly ILogger<RequestPostHocScoringHandler> _logger;

    public RequestPostHocScoringHandler(
        IAgentExecutionRepository executionRepository,
        IEvalRunRepository runRepository,
        IEvalRunQueue queue,
        IOptions<EvalOptions> options,
        ILogger<RequestPostHocScoringHandler> logger)
    {
        _executionRepository = executionRepository;
        _runRepository = runRepository;
        _queue = queue;
        _options = options;
        _logger = logger;
    }

    public async Task<RequestPostHocScoringResponse> Handle(
        RequestPostHocScoringCommand request, CancellationToken cancellationToken)
    {
        if (request.ScorerType != EvalScorerType.LlmJudge)
            throw new ValidationException(
                nameof(request.ScorerType),
                "Post-hoc scoring is judge-only — RuleBasedScorer requires a predefined EvalCase's " +
                "ExpectedCriteria, which doesn't exist for arbitrary historical traces. See ADR-013.");

        if (string.IsNullOrWhiteSpace(request.Rubric))
            throw new ValidationException(nameof(request.Rubric), "Rubric is required for post-hoc judge scoring.");

        var hasDateRange = request.DateFrom.HasValue && request.DateTo.HasValue;
        var hasExplicitIds = request.TraceIds is { Count: > 0 };
        if (!hasDateRange && !hasExplicitIds)
            throw new ValidationException(
                nameof(request.DateFrom),
                "A post-hoc scoring request must specify either a date range (DateFrom and DateTo) " +
                "or an explicit TraceIds list — AgentType alone does not bound the selection.");

        if (request.MaxTraces <= 0 || request.MaxTraces > _options.Value.MaxPostHocTracesPerRequestCeiling)
            throw new ValidationException(
                nameof(request.MaxTraces),
                $"MaxTraces must be between 1 and {_options.Value.MaxPostHocTracesPerRequestCeiling}.");

        var selection = await _executionRepository.SelectForPostHocScoringAsync(
            request.DateFrom, request.DateTo, request.AgentType, request.TraceIds, request.MaxTraces, cancellationToken)
            .ConfigureAwait(false);

        if (selection.TotalMatched > request.MaxTraces)
            throw new ValidationException(
                nameof(request.MaxTraces),
                $"Selection matched {selection.TotalMatched} traces, exceeding the requested cap of " +
                $"{request.MaxTraces}. Narrow the date range or trace ID list.");

        if (selection.AgentExecutionIds.Count == 0)
            throw new ValidationException(nameof(request.TraceIds), "No completed traces matched the selection criteria.");

        var selectionCriteriaJson = JsonSerializer.Serialize(new
        {
            dateFrom = request.DateFrom,
            dateTo = request.DateTo,
            agentType = request.AgentType?.ToString(),
            explicitTraceIds = request.TraceIds,
            resolvedTraceIds = selection.AgentExecutionIds,
            passThreshold = request.PassThreshold
        });

        var subjectVersion = $"posthoc-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}";
        var run = EvalRun.CreatePostHoc(subjectVersion, request.Rubric, selectionCriteriaJson, request.ForceRescore);
        await _runRepository.AddAsync(run, cancellationToken).ConfigureAwait(false);
        await _queue.EnqueueAsync(run.Id, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Enqueued post-hoc scoring run {RunId} for {TraceCount} traces (agentType={AgentType})",
            run.Id, selection.AgentExecutionIds.Count, request.AgentType);

        return new RequestPostHocScoringResponse(
            run.Id, run.Status.ToString(), selection.AgentExecutionIds.Count, run.TriggeredAt);
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~RequestPostHocScoringHandlerTests"`
Expected: PASS, all 8 tests green — including `Handle_NoDateRangeAndNoTraceIds_ThrowsValidation_AgentTypeAloneDoesNotBoundSelection`, which is the required "unbounded selection is rejected, not silently scoped to everything" test, and `Handle_ForceRescoreTrue_ThreadsFlagOntoCreatedRun`.

- [ ] **Step 8: Commit**

```bash
git add src/OrchestAI.Application/Configuration/EvalOptions.cs \
  src/OrchestAI.Infrastructure/OrchestAI.Infrastructure.csproj \
  src/OrchestAI.Infrastructure/Eval/LlmJudgeScorer.cs \
  src/OrchestAI.Infrastructure/DependencyInjection.cs \
  src/OrchestAI.Application/Commands/RequestPostHocScoring/ \
  tests/OrchestAI.Tests/Application/RequestPostHocScoringHandlerTests.cs
git commit -m "feat: add RequestPostHocScoringCommand with bounded trace selection and judge-only validation

Relocates EvalOptions from Infrastructure to Application (with a new
Infrastructure->Application project reference) since the post-hoc scoring
handler lives in Application and this codebase's layering never allows
Application to depend on Infrastructure."
```

Note: `git mv` in Step 3 already staged the file move; `git add` on the new path here is for any further edits made to it (the new property) plus the newly-created/modified files.

---

### Task 5: `EvalRunBackgroundWorker` post-hoc branch

**Files:**
- Modify: `src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs`
- Test: Create `tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerPostHocTests.cs`

**Interfaces:**
- Consumes: `EvalRun.Source`, `EvalRun.SelectionCriteriaJson`, `EvalRun.Rubric`, `EvalRun.ForceRescore`, `EvalRun.IncrementSkippedCount` (Task 1); `IEvalResultRepository.GetScoredAgentExecutionIdsAsync`, `IEvalResultRepository.DeletePostHocResultAsync` (Task 3); `EvalCase.CreateEphemeral` (Task 1); `IAgentExecutionRepository.GetByIdAsync` (existing).
- Produces: `EvalRunBackgroundWorker.ProcessRunAsync` now branches on `run.Source`; internal `ProcessPostHocRunAsync`, which itself branches on `run.ForceRescore` — default path checks `GetScoredAgentExecutionIdsAsync` and skips matches (incrementing `SkippedAlreadyScoredCount`); `ForceRescore` path skips that check entirely and calls `DeletePostHocResultAsync` before each insert instead (supersede, not skip, not append).

- [ ] **Step 1: Write the failing worker tests**

Create `tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerPostHocTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Application.Configuration;
using OrchestAI.Infrastructure.Eval;

namespace OrchestAI.Tests.Infrastructure;

public sealed class EvalRunBackgroundWorkerPostHocTests
{
    private static LlmJudgeScorer BuildRealJudgeScorer(string judgeResponseJson, Mock<ICostLedgerRepository> costRepoMock)
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

        costRepoMock
            .Setup(r => r.AddAsync(It.IsAny<CostLedger>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001",
            DefaultJudgePassThreshold = 0.7m
        });

        return new LlmJudgeScorer(factoryMock.Object, pricingCacheMock.Object, costRepoMock.Object, options);
    }

    private static EvalRunBackgroundWorker BuildWorker(
        IEvalRunRepository runRepo, IEvalResultRepository resultRepo, IAgentExecutionRepository executionRepo,
        LlmJudgeScorer judgeScorer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton(runRepo);
        services.AddSingleton(resultRepo);
        services.AddSingleton(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton(executionRepo);
        services.AddSingleton(Mock.Of<IAgentFactory>());
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([judgeScorer]));
        var provider = services.BuildServiceProvider();

        var queueMock = new Mock<IEvalRunQueue>();
        return new EvalRunBackgroundWorker(
            queueMock.Object, provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EvalRunBackgroundWorker>.Instance);
    }

    private static EvalRun BuildPostHocRun(params Guid[] resolvedTraceIds)
    {
        var criteriaJson = System.Text.Json.JsonSerializer.Serialize(new { resolvedTraceIds });
        return EvalRun.CreatePostHoc("posthoc-1", "was the tool call appropriate?", criteriaJson);
    }

    [Fact]
    public async Task ProcessRunAsync_PostHocRun_ScoresEachTraceWithNullEvalCaseIdAndRubric()
    {
        var taskId = Guid.NewGuid();
        var executionA = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        executionA.Start();
        executionA.Complete("output A", 10, 5, 0.01m);
        var executionB = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        executionB.Start();
        executionB.Complete("output B", 10, 5, 0.01m);

        var run = BuildPostHocRun(executionA.Id, executionB.Id);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock
            .Setup(r => r.GetScoredAgentExecutionIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), EvalScorerType.LlmJudge, LlmJudgeScorer.Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid>());
        var captured = new List<EvalResult>();
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback<EvalResult, CancellationToken>((r, _) => captured.Add(r))
            .Returns(Task.CompletedTask);

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock.Setup(r => r.GetByIdAsync(executionA.Id, It.IsAny<CancellationToken>())).ReturnsAsync(executionA);
        executionRepoMock.Setup(r => r.GetByIdAsync(executionB.Id, It.IsAny<CancellationToken>())).ReturnsAsync(executionB);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        var judgeScorer = BuildRealJudgeScorer("""{"score":0.9,"reasoning":"looks correct"}""", costRepoMock);

        var worker = BuildWorker(runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, judgeScorer);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.Status.Should().Be(EvalRunStatus.Completed);
        run.SkippedAlreadyScoredCount.Should().Be(0);
        captured.Should().HaveCount(2);
        captured.Should().OnlyContain(r => r.EvalCaseId == null);
        captured.Should().OnlyContain(r => r.Rubric == "was the tool call appropriate?");
        captured.Select(r => r.AgentExecutionId).Should().BeEquivalentTo([executionA.Id, executionB.Id]);
        costRepoMock.Verify(
            r => r.AddAsync(It.Is<CostLedger>(c => c.Source == CostSource.Eval && c.EvalRunId == run.Id),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
        resultRepoMock.Verify(
            r => r.DeletePostHocResultAsync(
                It.IsAny<Guid>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the default (non-force) path never supersedes — it only skips or inserts");
    }

    [Fact]
    public async Task ProcessRunAsync_PostHocRun_SkipsAlreadyScoredTracesAndCountsThem()
    {
        var taskId = Guid.NewGuid();
        var alreadyScored = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        alreadyScored.Start();
        alreadyScored.Complete("output", 10, 5, 0.01m);
        var notYetScored = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        notYetScored.Start();
        notYetScored.Complete("output", 10, 5, 0.01m);

        var run = BuildPostHocRun(alreadyScored.Id, notYetScored.Id);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock
            .Setup(r => r.GetScoredAgentExecutionIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), EvalScorerType.LlmJudge, LlmJudgeScorer.Version, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Guid> { alreadyScored.Id });
        var addCallCount = 0;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback(() => addCallCount++)
            .Returns(Task.CompletedTask);

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock.Setup(r => r.GetByIdAsync(notYetScored.Id, It.IsAny<CancellationToken>())).ReturnsAsync(notYetScored);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        var judgeScorer = BuildRealJudgeScorer("""{"score":0.9,"reasoning":"looks correct"}""", costRepoMock);

        var worker = BuildWorker(runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, judgeScorer);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        run.SkippedAlreadyScoredCount.Should().Be(1);
        addCallCount.Should().Be(1);
        executionRepoMock.Verify(r => r.GetByIdAsync(alreadyScored.Id, It.IsAny<CancellationToken>()), Times.Never);
        resultRepoMock.Verify(
            r => r.DeletePostHocResultAsync(
                It.IsAny<Guid>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the default (non-force) path skips the already-scored trace outright — it never supersedes");
    }

    [Fact]
    public async Task ProcessRunAsync_ForceRescoreTrue_SupersedesPriorResultWithoutSkippingOrConsultingIdempotencyCheck()
    {
        var taskId = Guid.NewGuid();
        var execution = AgentExecution.Create(taskId, AgentType.Research, "prompt");
        execution.Start();
        execution.Complete("output", 10, 5, 0.01m);

        var criteriaJson = System.Text.Json.JsonSerializer.Serialize(new { resolvedTraceIds = new[] { execution.Id } });
        var run = EvalRun.CreatePostHoc("posthoc-1", "was the tool call appropriate?", criteriaJson, forceRescore: true);

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);
        runRepoMock.Setup(r => r.UpdateAsync(It.IsAny<EvalRun>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        var deleteCallCount = 0;
        resultRepoMock
            .Setup(r => r.DeletePostHocResultAsync(
                execution.Id, EvalScorerType.LlmJudge, LlmJudgeScorer.Version, It.IsAny<CancellationToken>()))
            .Callback(() => deleteCallCount++)
            .Returns(Task.CompletedTask);
        var addCallCount = 0;
        resultRepoMock
            .Setup(r => r.AddAsync(It.IsAny<EvalResult>(), It.IsAny<CancellationToken>()))
            .Callback(() => addCallCount++)
            .Returns(Task.CompletedTask);

        var executionRepoMock = new Mock<IAgentExecutionRepository>();
        executionRepoMock.Setup(r => r.GetByIdAsync(execution.Id, It.IsAny<CancellationToken>())).ReturnsAsync(execution);

        var costRepoMock = new Mock<ICostLedgerRepository>();
        var judgeScorer = BuildRealJudgeScorer("""{"score":0.95,"reasoning":"corrected score"}""", costRepoMock);

        var worker = BuildWorker(runRepoMock.Object, resultRepoMock.Object, executionRepoMock.Object, judgeScorer);

        await worker.ProcessRunAsync(run.Id, CancellationToken.None);

        deleteCallCount.Should().Be(1, "ForceRescore supersedes the prior result instead of appending");
        addCallCount.Should().Be(1);
        run.SkippedAlreadyScoredCount.Should().Be(0, "a superseded trace was not skipped, it was re-scored");
        resultRepoMock.Verify(
            r => r.GetScoredAgentExecutionIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<EvalScorerType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "ForceRescore bypasses the idempotency pre-check entirely — it doesn't need to know what was already scored");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~EvalRunBackgroundWorkerPostHocTests"`
Expected: FAIL — `run.Source == EvalRunSource.PostHoc` branch doesn't exist yet, so a post-hoc run currently falls into the live-suite path and throws trying to fetch a suite for a null `SuiteId`.

- [ ] **Step 3: Update `EvalRunBackgroundWorker`**

In `src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs`, add `using System.Linq;` is not needed (implicit usings cover it), but add `using OrchestAI.Domain.Enums;` if not already present (it likely already has it transitively via other usings — check and add explicitly if the build fails). Replace the body of `ProcessRunAsync` from:

```csharp
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

        try
        {
```

to:

```csharp
    internal async Task ProcessRunAsync(Guid evalRunId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var runRepository = scope.ServiceProvider.GetRequiredService<IEvalRunRepository>();
        var suiteRepository = scope.ServiceProvider.GetRequiredService<IEvalSuiteRepository>();
        var resultRepository = scope.ServiceProvider.GetRequiredService<IEvalResultRepository>();
        var taskRepository = scope.ServiceProvider.GetRequiredService<IOrchestrationTaskRepository>();
        var executionRepository = scope.ServiceProvider.GetRequiredService<IAgentExecutionRepository>();
        var agentFactory = scope.ServiceProvider.GetRequiredService<IAgentFactory>();
        var scorerFactory = scope.ServiceProvider.GetRequiredService<IEvalScorerFactory>();

        var run = await runRepository.GetByIdAsync(evalRunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            _logger.LogWarning("Eval run {RunId} not found, skipping", evalRunId);
            return;
        }

        if (run.Source == EvalRunSource.PostHoc)
        {
            await ProcessPostHocRunAsync(run, runRepository, resultRepository, executionRepository, scorerFactory, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        try
        {
```

(The rest of the existing `try`/`catch` body for the live-suite path is unchanged.)

Then add these two new private methods at the end of the class, before the closing brace:

```csharp
    private async Task ProcessPostHocRunAsync(
        EvalRun run,
        IEvalRunRepository runRepository,
        IEvalResultRepository resultRepository,
        IAgentExecutionRepository executionRepository,
        IEvalScorerFactory scorerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            var traceIds = ParseResolvedTraceIds(run.SelectionCriteriaJson!);
            List<Guid> tracesToScore;

            if (run.ForceRescore)
            {
                // Deliberate override — every resolved trace is (re-)scored regardless of prior
                // results. Nothing here counts as "skipped" (that field means "left unscored");
                // a prior result is superseded per-trace below instead. See ADR-013 confirmation #3.
                tracesToScore = traceIds;
            }
            else
            {
                var alreadyScored = await resultRepository.GetScoredAgentExecutionIdsAsync(
                    traceIds, EvalScorerType.LlmJudge, LlmJudgeScorer.Version, cancellationToken).ConfigureAwait(false);
                var alreadyScoredSet = alreadyScored.ToHashSet();

                foreach (var id in traceIds.Where(alreadyScoredSet.Contains))
                    run.IncrementSkippedCount();

                tracesToScore = traceIds.Where(id => !alreadyScoredSet.Contains(id)).ToList();
            }

            run.MarkRunning();
            await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

            var scorer = scorerFactory.Resolve(EvalScorerType.LlmJudge);
            var passThreshold = ParsePassThreshold(run.SelectionCriteriaJson!);
            var ephemeralCase = EvalCase.CreateEphemeral(run.Rubric!, passThreshold);

            foreach (var executionId in tracesToScore)
            {
                try
                {
                    var execution = await executionRepository.GetByIdAsync(executionId, cancellationToken).ConfigureAwait(false);
                    if (execution is null || execution.Status != ExecutionStatus.Completed || execution.OutputResult is null)
                    {
                        _logger.LogWarning(
                            "Post-hoc run {RunId}: trace {ExecutionId} no longer eligible, skipping", run.Id, executionId);
                        continue;
                    }

                    if (run.ForceRescore)
                    {
                        // Supersede, not append — deletes any prior result for this exact
                        // (trace, scorer, version) tuple before inserting, so the partial unique
                        // index from Task 1 is never violated by a deliberate re-score.
                        await resultRepository.DeletePostHocResultAsync(
                            executionId, EvalScorerType.LlmJudge, LlmJudgeScorer.Version, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    var context = new EvalScoringContext(execution.OrchestrationTaskId, run.Id);
                    var scoreResult = await scorer.ScoreAsync(ephemeralCase, execution.OutputResult, context, cancellationToken)
                        .ConfigureAwait(false);

                    var evalResult = EvalResult.Create(
                        run.Id, evalCaseId: null, execution.Id, EvalScorerType.LlmJudge,
                        scoreResult.ScorerVersion, scoreResult.Score, scoreResult.Passed, scoreResult.ScorerOutputJson,
                        rubric: run.Rubric);
                    await resultRepository.AddAsync(evalResult, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex, "Post-hoc run {RunId}: trace {ExecutionId} failed unexpectedly, continuing", run.Id, executionId);
                }
            }

            run.MarkCompleted();
            await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Post-hoc run {RunId} completed ({ScoredCount} scored, {SkippedCount} skipped as already-scored, forceRescore={ForceRescore})",
                run.Id, tracesToScore.Count, run.SkippedAlreadyScoredCount, run.ForceRescore);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.MarkFailed(ex.Message);
            await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Post-hoc run {RunId} failed unexpectedly outside per-trace handling", run.Id);
        }
    }

    private static List<Guid> ParseResolvedTraceIds(string selectionCriteriaJson)
    {
        using var doc = JsonDocument.Parse(selectionCriteriaJson);
        return doc.RootElement.GetProperty("resolvedTraceIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
    }

    private static decimal? ParsePassThreshold(string selectionCriteriaJson)
    {
        using var doc = JsonDocument.Parse(selectionCriteriaJson);
        return doc.RootElement.TryGetProperty("passThreshold", out var el) && el.ValueKind != JsonValueKind.Null
            ? el.GetDecimal()
            : null;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~EvalRunBackgroundWorkerPostHocTests|FullyQualifiedName~EvalRunBackgroundWorkerTests"`
Expected: PASS — the default-path post-hoc tests, the new `ForceRescore` supersede test, and the existing Week 8 live-suite tests (which must be unaffected by the branch added).

- [ ] **Step 5: Commit**

```bash
git add src/OrchestAI.Infrastructure/Eval/EvalRunBackgroundWorker.cs \
  tests/OrchestAI.Tests/Infrastructure/EvalRunBackgroundWorkerPostHocTests.cs
git commit -m "feat: add post-hoc scoring branch to EvalRunBackgroundWorker with idempotency skip and ForceRescore supersede path"
```

---

### Task 6: `GetPostHocScoringSummaryQuery`

**Files:**
- Create: `src/OrchestAI.Application/Queries/GetPostHocScoringSummary/GetPostHocScoringSummaryQuery.cs`
- Create: `src/OrchestAI.Application/Queries/GetPostHocScoringSummary/GetPostHocScoringSummaryResponse.cs`
- Create: `src/OrchestAI.Application/Queries/GetPostHocScoringSummary/GetPostHocScoringSummaryHandler.cs`
- Test: Create `tests/OrchestAI.Tests/Application/GetPostHocScoringSummaryHandlerTests.cs`

**Interfaces:**
- Consumes: `IEvalRunRepository.GetByIdAsync`, `IEvalResultRepository.GetByRunIdAsync`, `EvalRun.Source`, `EvalRun.SkippedAlreadyScoredCount`.
- Produces: `GetPostHocScoringSummaryQuery(Guid EvalRunId) : IRequest<GetPostHocScoringSummaryResponse>`; `GetPostHocScoringSummaryResponse(Guid EvalRunId, string Status, int ScoredCount, int SkippedAlreadyScoredCount, int PassedCount, decimal PassRate, IReadOnlyList<ScoreDistributionBucketDto> ScoreDistribution, DateTimeOffset TriggeredAt, DateTimeOffset? CompletedAt)`; `ScoreDistributionBucketDto(string Range, int Count)`.

- [ ] **Step 1: Write the failing handler tests**

Create `tests/OrchestAI.Tests/Application/GetPostHocScoringSummaryHandlerTests.cs`:

```csharp
using FluentAssertions;
using Moq;
using OrchestAI.Application.Exceptions;
using OrchestAI.Application.Queries.GetPostHocScoringSummary;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Tests.Application;

public sealed class GetPostHocScoringSummaryHandlerTests
{
    [Fact]
    public async Task Handle_RunNotFound_ThrowsNotFound()
    {
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((EvalRun?)null);

        var handler = new GetPostHocScoringSummaryHandler(runRepoMock.Object, Mock.Of<IEvalResultRepository>());

        var act = async () => await handler.Handle(new GetPostHocScoringSummaryQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_LiveSuiteRun_ThrowsValidation_SummaryOnlyAppliesToPostHocRuns()
    {
        var liveRun = EvalRun.Create(Guid.NewGuid(), "commit-abc", null);
        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(liveRun.Id, It.IsAny<CancellationToken>())).ReturnsAsync(liveRun);

        var handler = new GetPostHocScoringSummaryHandler(runRepoMock.Object, Mock.Of<IEvalResultRepository>());

        var act = async () => await handler.Handle(new GetPostHocScoringSummaryQuery(liveRun.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Handle_PostHocRunWithMixedResults_ComputesPassRateAndDistribution()
    {
        var run = EvalRun.CreatePostHoc("posthoc-1", "rubric", "{\"resolvedTraceIds\":[]}");
        run.IncrementSkippedCount();
        run.MarkCompleted();

        var results = new List<EvalResult>
        {
            EvalResult.Create(run.Id, null, Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", 0.9m, true, "{}"),
            EvalResult.Create(run.Id, null, Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", 0.3m, false, "{}"),
            EvalResult.Create(run.Id, null, Guid.NewGuid(), EvalScorerType.LlmJudge, "llm-judge-v1", 0.95m, true, "{}"),
        };

        var runRepoMock = new Mock<IEvalRunRepository>();
        runRepoMock.Setup(r => r.GetByIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(run);

        var resultRepoMock = new Mock<IEvalResultRepository>();
        resultRepoMock.Setup(r => r.GetByRunIdAsync(run.Id, It.IsAny<CancellationToken>())).ReturnsAsync(results);

        var handler = new GetPostHocScoringSummaryHandler(runRepoMock.Object, resultRepoMock.Object);

        var response = await handler.Handle(new GetPostHocScoringSummaryQuery(run.Id), CancellationToken.None);

        response.ScoredCount.Should().Be(3);
        response.SkippedAlreadyScoredCount.Should().Be(1);
        response.PassedCount.Should().Be(2);
        response.PassRate.Should().BeApproximately(2m / 3m, 0.0001m);
        response.ScoreDistribution.Sum(b => b.Count).Should().Be(3);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~GetPostHocScoringSummaryHandlerTests"`
Expected: FAIL — types don't exist (compile error).

- [ ] **Step 3: Create the query and response records**

`src/OrchestAI.Application/Queries/GetPostHocScoringSummary/GetPostHocScoringSummaryQuery.cs`:

```csharp
using MediatR;

namespace OrchestAI.Application.Queries.GetPostHocScoringSummary;

public sealed record GetPostHocScoringSummaryQuery(Guid EvalRunId) : IRequest<GetPostHocScoringSummaryResponse>;
```

`src/OrchestAI.Application/Queries/GetPostHocScoringSummary/GetPostHocScoringSummaryResponse.cs`:

```csharp
namespace OrchestAI.Application.Queries.GetPostHocScoringSummary;

public sealed record GetPostHocScoringSummaryResponse(
    Guid EvalRunId,
    string Status,
    int ScoredCount,
    int SkippedAlreadyScoredCount,
    int PassedCount,
    decimal PassRate,
    IReadOnlyList<ScoreDistributionBucketDto> ScoreDistribution,
    DateTimeOffset TriggeredAt,
    DateTimeOffset? CompletedAt);

public sealed record ScoreDistributionBucketDto(string Range, int Count);
```

- [ ] **Step 4: Create the handler**

```csharp
using MediatR;
using OrchestAI.Application.Exceptions;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;

namespace OrchestAI.Application.Queries.GetPostHocScoringSummary;

public sealed class GetPostHocScoringSummaryHandler
    : IRequestHandler<GetPostHocScoringSummaryQuery, GetPostHocScoringSummaryResponse>
{
    private static readonly (decimal Lower, decimal Upper)[] Buckets =
    [
        (0.0m, 0.2m), (0.2m, 0.4m), (0.4m, 0.6m), (0.6m, 0.8m), (0.8m, 1.0m)
    ];

    private readonly IEvalRunRepository _runRepository;
    private readonly IEvalResultRepository _resultRepository;

    public GetPostHocScoringSummaryHandler(IEvalRunRepository runRepository, IEvalResultRepository resultRepository)
    {
        _runRepository = runRepository;
        _resultRepository = resultRepository;
    }

    public async Task<GetPostHocScoringSummaryResponse> Handle(
        GetPostHocScoringSummaryQuery request, CancellationToken cancellationToken)
    {
        var run = await _runRepository.GetByIdAsync(request.EvalRunId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException(nameof(EvalRun), request.EvalRunId);

        if (run.Source != EvalRunSource.PostHoc)
            throw new ValidationException(nameof(run.Source), $"Eval run {run.Id} is not a post-hoc run.");

        var results = await _resultRepository.GetByRunIdAsync(run.Id, cancellationToken).ConfigureAwait(false);
        var passedCount = results.Count(r => r.Passed);

        var distribution = Buckets.Select(b => new ScoreDistributionBucketDto(
            $"{b.Lower:0.0}-{b.Upper:0.0}",
            results.Count(r => r.Score >= b.Lower && (b.Upper == 1.0m ? r.Score <= b.Upper : r.Score < b.Upper))))
            .ToList();

        var passRate = results.Count == 0 ? 0m : (decimal)passedCount / results.Count;

        return new GetPostHocScoringSummaryResponse(
            run.Id, run.Status.ToString(), results.Count, run.SkippedAlreadyScoredCount,
            passedCount, passRate, distribution, run.TriggeredAt, run.CompletedAt);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~GetPostHocScoringSummaryHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/OrchestAI.Application/Queries/GetPostHocScoringSummary/ \
  tests/OrchestAI.Tests/Application/GetPostHocScoringSummaryHandlerTests.cs
git commit -m "feat: add GetPostHocScoringSummaryQuery with pass rate and score distribution"
```

---

### Task 7: Controller endpoints

**Files:**
- Modify: `src/OrchestAI.API/Controllers/EvalsController.cs`

**Interfaces:**
- Consumes: `RequestPostHocScoringCommand`, `RequestPostHocScoringResponse`, `GetPostHocScoringSummaryQuery`, `GetPostHocScoringSummaryResponse`.

- [ ] **Step 1: Add the two endpoints**

In `src/OrchestAI.API/Controllers/EvalsController.cs`, add these usings:

```csharp
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Application.Queries.GetPostHocScoringSummary;
```

Add this record near `TriggerEvalRunRequest`:

```csharp
    public sealed record RequestPostHocScoringRequest(
        DateTimeOffset? DateFrom, DateTimeOffset? DateTo, AgentType? AgentType, IReadOnlyList<Guid>? TraceIds,
        EvalScorerType ScorerType, string Rubric, decimal? PassThreshold, int MaxTraces, bool ForceRescore = false);
```

Add these two actions at the end of the controller, before the closing brace:

```csharp
    /// <summary>Requests post-hoc scoring of historical AgentExecution traces (Week 9) — judge-only, no re-execution.</summary>
    [HttpPost("/api/v1/post-hoc-scoring")]
    [ProducesResponseType(typeof(RequestPostHocScoringResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RequestPostHocScoringAsync(
        [FromBody] RequestPostHocScoringRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(
                new RequestPostHocScoringCommand(
                    request.DateFrom, request.DateTo, request.AgentType, request.TraceIds,
                    request.ScorerType, request.Rubric, request.PassThreshold, request.MaxTraces,
                    request.ForceRescore),
                cancellationToken);
            return AcceptedAtAction("GetPostHocScoringSummary", new { runId = response.EvalRunId }, response);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for RequestPostHocScoring: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }

    /// <summary>Gets pass rate / score distribution for a completed post-hoc scoring run.</summary>
    [HttpGet("/api/v1/eval-runs/{runId:guid}/posthoc-summary")]
    [ProducesResponseType(typeof(GetPostHocScoringSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPostHocScoringSummaryAsync(Guid runId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _mediator.Send(new GetPostHocScoringSummaryQuery(runId), cancellationToken);
            return Ok(response);
        }
        catch (NotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed for GetPostHocScoringSummary: {@Errors}", ex.Errors);
            return ValidationProblem(new ValidationProblemDetails(
                ex.Errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)));
        }
    }
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build OrchestAI.sln`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/OrchestAI.API/Controllers/EvalsController.cs
git commit -m "feat: add post-hoc scoring trigger and summary endpoints to EvalsController"
```

---

### Task 8: Integration test — end-to-end post-hoc scoring of seeded historical traces

**Files:**
- Test: Create `tests/OrchestAI.Tests/Integration/PostHocScoringIntegrationTests.cs`

**Interfaces:**
- Consumes: `RequestPostHocScoringHandler`, `EvalRunBackgroundWorker.ProcessRunAsync`, `EvalResultRepository`, `EvalRunRepository`, `AgentExecutionRepository` — all against a shared in-memory `AppDbContext`, exercising the full command → enqueue-equivalent → worker → persisted-result path without any mocks on the data layer.

- [ ] **Step 1: Write the integration test**

Create `tests/OrchestAI.Tests/Integration/PostHocScoringIntegrationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OrchestAI.Application.Commands.RequestPostHocScoring;
using OrchestAI.Domain.Entities;
using OrchestAI.Domain.Enums;
using OrchestAI.Domain.Interfaces;
using OrchestAI.Domain.Models;
using OrchestAI.Application.Configuration;
using OrchestAI.Infrastructure.Data;
using OrchestAI.Infrastructure.Eval;
using OrchestAI.Infrastructure.Repositories;

namespace OrchestAI.Tests.Integration;

// End-to-end: seeds AgentExecution rows exactly as real production traffic would create them
// (never via a live eval run), submits a real post-hoc scoring request through the real command
// handler, processes it through the real background worker, and asserts on the real persisted
// EvalResult rows — no mocked repositories anywhere in this test.
public sealed class PostHocScoringIntegrationTests
{
    private static PooledDbContextFactory<AppDbContext> BuildFactory(string dbName)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;
        return new PooledDbContextFactory<AppDbContext>(options);
    }

    [Fact]
    public async Task FullFlow_SeededHistoricalTraces_ProducesEvalResultsWithNullCaseIdAndRubric()
    {
        var dbName = Guid.NewGuid().ToString();
        var factory = BuildFactory(dbName);

        var user = TestUserFactory.Create("posthoc-integration@test.local");
        var task = OrchestrationTask.Create(user.Id, "production task", "prompt");
        var executionOne = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        executionOne.Start();
        executionOne.Complete("Researched topic X thoroughly.", 120, 60, 0.02m);
        var executionTwo = AgentExecution.Create(task.Id, AgentType.Research, "prompt");
        executionTwo.Start();
        executionTwo.Complete("Researched topic Y thoroughly.", 130, 65, 0.02m);

        await using (var seedCtx = await factory.CreateDbContextAsync())
        {
            seedCtx.Users.Add(user);
            seedCtx.OrchestrationTasks.Add(task);
            seedCtx.AgentExecutions.AddRange(executionOne, executionTwo);
            await seedCtx.SaveChangesAsync();
        }

        var executionRepository = new AgentExecutionRepository(factory);
        var runRepository = new EvalRunRepository(factory);
        var resultRepository = new EvalResultRepository(factory);
        var queue = new InMemoryEvalRunQueue();

        var evalOptions = Options.Create(new EvalOptions { MaxPostHocTracesPerRequestCeiling = 500 });
        var requestHandler = new RequestPostHocScoringHandler(
            executionRepository, runRepository, queue, evalOptions, NullLogger<RequestPostHocScoringHandler>.Instance);

        var command = new RequestPostHocScoringCommand(
            DateFrom: DateTimeOffset.UtcNow.AddDays(-1), DateTo: DateTimeOffset.UtcNow.AddDays(1),
            AgentType: AgentType.Research, TraceIds: null, ScorerType: EvalScorerType.LlmJudge,
            Rubric: "Did the agent thoroughly research the requested topic?", PassThreshold: 0.6m, MaxTraces: 10);

        var triggerResponse = await requestHandler.Handle(command, CancellationToken.None);
        triggerResponse.ResolvedTraceCount.Should().Be(2);

        var services = new ServiceCollection();
        services.AddSingleton<IEvalSuiteRepository>(Mock.Of<IEvalSuiteRepository>());
        services.AddSingleton<IEvalRunRepository>(runRepository);
        services.AddSingleton<IEvalResultRepository>(resultRepository);
        services.AddSingleton<IOrchestrationTaskRepository>(Mock.Of<IOrchestrationTaskRepository>());
        services.AddSingleton<IAgentExecutionRepository>(executionRepository);
        services.AddSingleton<IAgentFactory>(Mock.Of<IAgentFactory>());

        var providerMock = new Mock<ILlmProvider>();
        providerMock.Setup(p => p.ProviderId).Returns("anthropic");
        providerMock
            .Setup(p => p.SendAsync(It.IsAny<AgentConversation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentTurn("end_turn", """{"score":0.85,"reasoning":"Thorough research shown."}""", [], 200, 40));
        var providerFactoryMock = new Mock<ILlmProviderFactory>();
        providerFactoryMock.Setup(f => f.Resolve("anthropic")).Returns(providerMock.Object);

        var pricingCacheMock = new Mock<IModelPricingCache>();
        pricingCacheMock
            .Setup(c => c.GetAsync("claude-haiku-4-5-20251001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ModelPricing.Create("claude-haiku-4-5-20251001", 0.80m, 4.00m));

        var costLedgerRepository = new CostLedgerRepository(factory);
        var judgeOptions = Options.Create(new EvalOptions
        {
            JudgeModel = "anthropic/claude-haiku-4-5-20251001", DefaultJudgePassThreshold = 0.7m
        });
        var judgeScorer = new LlmJudgeScorer(providerFactoryMock.Object, pricingCacheMock.Object, costLedgerRepository, judgeOptions);
        services.AddSingleton<IEvalScorerFactory>(new EvalScorerFactory([judgeScorer]));
        var provider = services.BuildServiceProvider();

        var worker = new EvalRunBackgroundWorker(
            Mock.Of<IEvalRunQueue>(), provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<EvalRunBackgroundWorker>.Instance);

        await worker.ProcessRunAsync(triggerResponse.EvalRunId, CancellationToken.None);

        var persistedResults = await resultRepository.GetByRunIdAsync(triggerResponse.EvalRunId, CancellationToken.None);
        persistedResults.Should().HaveCount(2);
        persistedResults.Should().OnlyContain(r => r.EvalCaseId == null);
        persistedResults.Should().OnlyContain(r => r.Rubric == "Did the agent thoroughly research the requested topic?");
        persistedResults.Should().OnlyContain(r => r.Passed);

        var run = await runRepository.GetByIdAsync(triggerResponse.EvalRunId, CancellationToken.None);
        run!.Status.Should().Be(EvalRunStatus.Completed);

        var costRows = await costLedgerRepository.GetDailyAggregatesAsync(
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow), CancellationToken.None);
        costRows.Should().BeEmpty("post-hoc judge cost is tagged Source=Eval and must not reach the production cost dashboard");
    }
}
```

Note: `IEvalRunQueue` is unused by the worker path here (the worker is driven directly via `ProcessRunAsync`, matching the existing `EvalRunBackgroundWorkerTests` convention) — `InMemoryEvalRunQueue` is only used to satisfy `RequestPostHocScoringHandler`'s constructor.

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/OrchestAI.Tests --filter "FullyQualifiedName~PostHocScoringIntegrationTests"`
Expected: PASS.

- [ ] **Step 3: Run the full test suite to confirm no regressions**

Run: `dotnet test tests/OrchestAI.Tests`
Expected: PASS, all tests green (Week 8 tests plus all Week 9 tests added above).

- [ ] **Step 4: Commit**

```bash
git add tests/OrchestAI.Tests/Integration/PostHocScoringIntegrationTests.cs
git commit -m "test: add end-to-end integration test for post-hoc scoring of historical traces"
```

---

### Task 9: Frontend — Post-Hoc tab in EvalsPage

**Files:**
- Modify: `frontend/src/EvalsPage.jsx`

**Interfaces:**
- Consumes: `POST /api/v1/post-hoc-scoring`, `GET /api/v1/eval-runs/{runId}/posthoc-summary`.

- [ ] **Step 1: Add the "Post-Hoc" tab to `SubNav`**

In `frontend/src/EvalsPage.jsx`, change the `tabs` array in `SubNav`:

```javascript
  const tabs = [['suites', 'Suites'], ['run', 'Run'], ['results', 'Results'], ['posthoc', 'Post-Hoc']]
```

- [ ] **Step 2: Add a `PostHocView` component**

Add this new component after `ResultsView` and before `export default function EvalsPage()`:

```javascript
function PostHocView() {
  const [dateFrom, setDateFrom] = useState('')
  const [dateTo, setDateTo] = useState('')
  const [agentType, setAgentType] = useState('')
  const [rubric, setRubric] = useState('')
  const [maxTraces, setMaxTraces] = useState(100)
  const [forceRescore, setForceRescore] = useState(false)
  const [error, setError] = useState(null)
  const [runId, setRunId] = useState(null)
  const [summary, setSummary] = useState(null)
  const [summaryError, setSummaryError] = useState(null)

  const agentTypes = ['Orchestrator', 'Research', 'Writer', 'Code', 'Data', 'Browser']

  const submit = () => {
    setError(null)
    setSummary(null)
    setSummaryError(null)
    fetch(`${API_BASE}/post-hoc-scoring`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        dateFrom: dateFrom ? new Date(dateFrom).toISOString() : null,
        dateTo: dateTo ? new Date(dateTo).toISOString() : null,
        agentType: agentType || null,
        traceIds: null,
        scorerType: 'LlmJudge',
        rubric,
        passThreshold: null,
        maxTraces: Number(maxTraces),
        forceRescore,
      }),
    })
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? `Failed: ${res.status}`)
        return res.json()
      })
      .then(data => setRunId(data.evalRunId))
      .catch(err => setError(err.message))
  }

  const refreshSummary = () => {
    if (!runId) return
    setSummaryError(null)
    fetch(`${API_BASE}/eval-runs/${runId}/posthoc-summary`)
      .then(async res => {
        if (!res.ok) throw new Error((await res.json()).title ?? `Failed: ${res.status}`)
        return res.json()
      })
      .then(setSummary)
      .catch(err => setSummaryError(err.message))
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      <div style={panelStyle}>
        <div style={labelStyle}>Score historical traces — judge-only, no re-execution</div>
        <div style={{ display: 'flex', gap: 12, alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div>
            <div style={labelStyle}>From</div>
            <input type="datetime-local" value={dateFrom} onChange={e => setDateFrom(e.target.value)} style={selectStyle} />
          </div>
          <div>
            <div style={labelStyle}>To</div>
            <input type="datetime-local" value={dateTo} onChange={e => setDateTo(e.target.value)} style={selectStyle} />
          </div>
          <div>
            <div style={labelStyle}>Agent type</div>
            <select value={agentType} onChange={e => setAgentType(e.target.value)} style={selectStyle}>
              <option value="">Any</option>
              {agentTypes.map(t => <option key={t} value={t}>{t}</option>)}
            </select>
          </div>
          <div>
            <div style={labelStyle}>Max traces</div>
            <input
              type="number" value={maxTraces} onChange={e => setMaxTraces(e.target.value)}
              style={{ ...selectStyle, width: 90 }}
            />
          </div>
        </div>
        <div style={{ marginTop: 10 }}>
          <div style={labelStyle}>Rubric</div>
          <textarea
            value={rubric}
            onChange={e => setRubric(e.target.value)}
            placeholder="e.g. Was the tool call appropriate given the user's request?"
            rows={3}
            style={{ ...selectStyle, width: '100%', resize: 'vertical' }}
          />
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 6, marginTop: 10, fontSize: 12, color: '#cdd6f4' }}>
          <input type="checkbox" checked={forceRescore} onChange={e => setForceRescore(e.target.checked)} />
          Force re-score (supersedes any prior post-hoc score for the same trace instead of skipping it)
        </label>
        <button onClick={submit} disabled={!rubric || !dateFrom || !dateTo} style={{ ...buttonStyle, marginTop: 10 }}>
          Submit post-hoc scoring request
        </button>
        {error && <p style={{ color: '#f38ba8', fontSize: 12, marginTop: 10 }}>{error}</p>}
      </div>

      {runId && (
        <div style={panelStyle}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div style={labelStyle}>Run {runId.slice(0, 8)}…</div>
            <button onClick={refreshSummary} style={buttonStyle}>Refresh summary</button>
          </div>
          {summaryError && <p style={{ color: '#6c7086', fontSize: 12 }}>{summaryError}</p>}
          {summary && (
            <div style={{ marginTop: 10 }}>
              <div style={{ fontSize: 13, color: '#cdd6f4' }}>
                Status: {summary.status} — {summary.scoredCount} scored, {summary.skippedAlreadyScoredCount} skipped
                (already scored)
              </div>
              <div style={{ fontSize: 20, fontWeight: 700, color: '#cdd6f4', marginTop: 6 }}>
                {(summary.passRate * 100).toFixed(0)}% pass rate ({summary.passedCount}/{summary.scoredCount})
              </div>
              <table style={{ width: '100%', marginTop: 12, borderCollapse: 'collapse', fontSize: 12 }}>
                <thead>
                  <tr style={{ color: '#6c7086', textAlign: 'left' }}>
                    <th style={{ padding: '4px 8px' }}>Score range</th>
                    <th style={{ padding: '4px 8px' }}>Count</th>
                  </tr>
                </thead>
                <tbody>
                  {summary.scoreDistribution.map(b => (
                    <tr key={b.range} style={{ borderTop: '1px solid #313244' }}>
                      <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{b.range}</td>
                      <td style={{ padding: '4px 8px', color: '#cdd6f4' }}>{b.count}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
```

- [ ] **Step 3: Wire the tab into `EvalsPage`**

Add this block inside `export default function EvalsPage()`, alongside the existing `subView === 'results'` block:

```javascript
      {subView === 'posthoc' && <PostHocView />}
```

- [ ] **Step 4: Manually verify in the browser**

Run: `cd frontend && npm run dev` (and the API via its usual local-run process), then open the app, navigate to the Evals tab → Post-Hoc sub-tab, fill in a date range and rubric against seeded historical traces, submit, and click "Refresh summary" to confirm the pass rate and distribution table render.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/EvalsPage.jsx
git commit -m "feat: add Post-Hoc scoring tab to EvalsPage"
```

---

### Task 10: ADR-013 and OBSERVABILITY.md

**Files:**
- Modify: `DECISIONS.md`
- Modify: `OBSERVABILITY.md`

- [ ] **Step 1: Append ADR-013 to `DECISIONS.md`**

Add after ADR-012's final line (`## Trigger for revisiting` section end):

```markdown

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

### Trigger for revisiting
- The first time `ForceRescore` is used against a large trace set — today it re-scores every
  resolved trace unconditionally (no partial "only rescore the ones that changed" mode); revisit
  if that becomes a real cost concern once there's usage data.
- The first post-hoc-eligible `IEvalScorer` other than `LlmJudge` (Decision 7).
- The first real post-hoc batch large enough that `MaxPostHocTracesPerRequestCeiling`'s default
  of 500 becomes a genuine throughput bottleneck rather than a safety rail.
```

- [ ] **Step 2: Add a "2b. Post-hoc scoring" section to `OBSERVABILITY.md`**

Insert after the `## 2a. Eval cost segregation` section (before `## 3. Query`):

```markdown

## 2b. Post-hoc scoring

Week 9 added `EvalRun.Source` (`LiveSuite`/`PostHoc`). `EvalResult` rows now originate from two
sources, told apart by `eval_run.source` (via the `EvalRunId` FK) together with
`EvalResult.EvalCaseId`:

- **Live suite execution** (Week 8): `EvalRun.Source = LiveSuite`, `EvalResult.EvalCaseId` is a
  real `EvalCase` FK, `EvalResult.Rubric` is null.
- **Post-hoc scoring** (Week 9): `EvalRun.Source = PostHoc`, `EvalResult.EvalCaseId` is null,
  `EvalResult.Rubric` holds the free-text judge rubric applied to that run's traces.

Post-hoc scoring never re-invokes an agent — `EvalRunBackgroundWorker.ProcessPostHocRunAsync`
reads `AgentExecution.OutputResult` directly from rows that already exist (production traffic or
past eval runs) and scores them with `LlmJudgeScorer` against a rubric instead of a predefined
`EvalCase`. Judge cost still flows through the same `Source=Eval`/`EvalRunId`-tagged `CostLedger`
path as Week 8 — no second cost-tagging code path was introduced.

Re-running a post-hoc request against an already-scored trace is a no-op by default (tracked in
`EvalRun.SkippedAlreadyScoredCount`). `EvalRun.ForceRescore` is the deliberate override — it
supersedes (delete-then-insert) rather than appends, so a trace never accumulates more than one
current post-hoc result.

Full reasoning: ADR-013 in `DECISIONS.md`.
```

- [ ] **Step 3: Commit**

```bash
git add DECISIONS.md OBSERVABILITY.md
git commit -m "docs: add ADR-013 for post-hoc scoring and update OBSERVABILITY.md"
```

---

## Self-review notes

- **Spec coverage:** All four blocking confirmations answered with a task-backed decision (Tasks 1, 3, 4, 5). Domain model changes (`eval_case_id` nullable, `EvalResult.rubric`, `EvalRun.source`, `EvalRun.forceRescore`) — Task 1. Confirmation #3's both-halves requirement (skip by default, distinct deliberate re-score path) is fully implemented, not deferred: `ForceRescore` flag (Tasks 1, 4, 7, 9) + `DeletePostHocResultAsync` supersede primitive (Task 3) + worker branch (Task 5), with tests proving the two paths are mutually exclusive (`DeletePostHocResultAsync` verified `Times.Never` in the default-path tests, `GetScoredAgentExecutionIdsAsync` verified `Times.Never` in the `ForceRescore` test). Commands/queries (`RequestPostHocScoringCommand`, `GetPostHocScoringSummaryQuery`) — Tasks 4, 6. Background service reuse — Task 5. Frontend — Task 9. All four required tests (unbounded-selection-rejected, idempotency, cost-tagging, integration seed test) are present in Tasks 4, 5, and 8. ADR-013 + `OBSERVABILITY.md` — Task 10.
- **Carried-forward items** (retention policy, Railway SDK pin, DESIGN_PRINCIPLES.md/issue labels) are explicitly out of scope per the spec and not included here.
- **Type consistency checked:** `EvalResult.Create`'s new `rubric` parameter is trailing-optional so both existing Week 8 call sites in `EvalRunBackgroundWorker` compile unchanged; `EvalCaseId` widening from `Guid` to `Guid?` is a compatible implicit conversion at every existing call site. `TraceSelectionResult`, `EvalRunSource`, `ScoreDistributionBucketDto` names are used identically across every task that references them.
