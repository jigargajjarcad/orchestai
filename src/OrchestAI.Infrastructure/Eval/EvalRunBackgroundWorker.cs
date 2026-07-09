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

        try
        {
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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.MarkFailed(ex.Message);
            await runRepository.UpdateAsync(run, cancellationToken).ConfigureAwait(false);
            _logger.LogError(ex, "Eval run {RunId} failed unexpectedly outside per-case handling", run.Id);
        }
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
