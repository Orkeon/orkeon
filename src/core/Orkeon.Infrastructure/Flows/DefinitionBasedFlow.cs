using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces;
using Orkeon.Domain.Common;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;

namespace Orkeon.Infrastructure.Flows;

/// <summary>
/// An IFlow implementation backed by an IFlowDefinition and IFlowStepExecutor.
/// Supports Sequential, Parallel, Conditional, and Loop execution modes.
/// </summary>
public partial class DefinitionBasedFlow : IFlow
{
    private readonly IFlowDefinition _definition;
    private readonly IFlowStepExecutor _executor;
    private readonly ILogger _logger;

    /// <inheritdoc />
    public FlowId Id => _definition.Id;
    /// <inheritdoc />
    public string Name => _definition.Name;
    /// <inheritdoc />
    public FlowState State { get; internal set; } = FlowState.Empty;

    /// <summary>Initializes a new instance of <see cref="DefinitionBasedFlow"/>.</summary>
    /// <param name="definition">The flow definition.</param>
    /// <param name="executor">The flow step executor.</param>
    /// <param name="logger">Optional logger.</param>
    public DefinitionBasedFlow(
        IFlowDefinition definition,
        IFlowStepExecutor executor,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        ArgumentNullException.ThrowIfNull(executor);
        _executor = executor;
        _logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Flow-execution fault barrier: any step/strategy failure is logged and converted into a failed FlowResult so a faulty flow does not crash the caller (cancellation is handled separately).")]
    public async Task<FlowResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return _definition.Type switch
            {
                FlowType.Sequential => await ExecuteSequentialAsync(cancellationToken).ConfigureAwait(false),
                FlowType.Parallel => await ExecuteParallelAsync(cancellationToken).ConfigureAwait(false),
                FlowType.Conditional => await ExecuteConditionalAsync(cancellationToken).ConfigureAwait(false),
                FlowType.Loop => await ExecuteLoopAsync(cancellationToken).ConfigureAwait(false),
                _ => await ExecuteSequentialAsync(cancellationToken).ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException)
        {
            return FlowResult.CreateFailure("Flow execution was cancelled.");
        }
        catch (Exception ex)
        {
            LogFlowFailed(ex, Name);
            return FlowResult.CreateFailure($"Flow execution failed: {ex.Message}");
        }
    }

    private async Task<FlowResult> ExecuteSequentialAsync(CancellationToken ct)
    {
        var steps = _definition.Steps.ToList();
        var currentIndex = 0;
        object? lastOutput = null;

        while (currentIndex < steps.Count)
        {
            ct.ThrowIfCancellationRequested();

            var stepDef = steps[currentIndex];
            var flowStep = _executor.ResolveStep(stepDef);

            var result = await ExecuteStepWithRetryAsync(flowStep, stepDef, ct).ConfigureAwait(false);

            if (!result.Success)
            {
                return FlowResult.CreateFailure($"Step '{stepDef.Name}' failed: {result.Error}");
            }

            // Merge updated context back into state
            State = MergeStepResult(State, stepDef.Name, result);
            lastOutput = result.Output;

            // Support NextStep branching
            if (!string.IsNullOrEmpty(result.NextStep))
            {
                var nextIndex = steps.FindIndex(s =>
                    s.Id.ToString() == result.NextStep || s.Name == result.NextStep);
                if (nextIndex >= 0)
                {
                    currentIndex = nextIndex;
                    continue;
                }
            }

            currentIndex++;
        }

        return new FlowResult
        {
            Success = true,
            Output = lastOutput,
            OutputState = State
        };
    }

    private async Task<FlowResult> ExecuteParallelAsync(CancellationToken ct)
    {
        // Topological sort using Kahn's algorithm for dependency-aware parallel execution
        var groups = TopologicalSort(_definition.Steps.ToList());
        object? lastOutput = null;

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var tasks = group.Select(async stepDef =>
            {
                var flowStep = _executor.ResolveStep(stepDef);
                var result = await ExecuteStepWithRetryAsync(flowStep, stepDef, ct).ConfigureAwait(false);
                return (stepDef, result);
            }).ToList();

            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            foreach (var (stepDef, result) in results)
            {
                if (!result.Success)
                {
                    return FlowResult.CreateFailure($"Step '{stepDef.Name}' failed: {result.Error}");
                }

                State = MergeStepResult(State, stepDef.Name, result);
                lastOutput = result.Output;
            }
        }

        return new FlowResult
        {
            Success = true,
            Output = lastOutput,
            OutputState = State
        };
    }

    private async Task<FlowResult> ExecuteConditionalAsync(CancellationToken ct)
    {
        object? lastOutput = null;

        foreach (var stepDef in _definition.Steps)
        {
            ct.ThrowIfCancellationRequested();

            if (!ShouldExecuteConditionalStep(stepDef))
                continue;

            var (result, failure) = await ExecuteStepOrFail(stepDef, ct).ConfigureAwait(false);
            if (failure != null)
                return failure;

            lastOutput = result!.Output;

            var branchResult = await ExecuteBranchStepIfNeeded(result!, ct).ConfigureAwait(false);
            if (branchResult?.Success == false)
                return FlowResult.CreateFailure(branchResult.Error ?? "Branch step failed");

            if (branchResult != null)
                lastOutput = branchResult.Output;
        }

        return new FlowResult { Success = true, Output = lastOutput, OutputState = State };
    }

    private bool ShouldExecuteConditionalStep(FlowStep stepDef)
    {
        var condition = stepDef.Parameters.Get<string>("condition");
        if (condition == null)
            return true;

        var conditionValue = State.Get<object>(condition);

        if (conditionValue is bool boolVal)
            return boolVal;
        if (conditionValue is string strVal)
            return !string.IsNullOrEmpty(strVal);

        return conditionValue is not null;
    }

    private async Task<(FlowStepResult?, FlowResult?)> ExecuteStepOrFail(FlowStep stepDef, CancellationToken ct)
    {
        var flowStep = _executor.ResolveStep(stepDef);
        var result = await ExecuteStepWithRetryAsync(flowStep, stepDef, ct).ConfigureAwait(false);

        if (!result.Success)
            return (null, FlowResult.CreateFailure($"Step '{stepDef.Name}' failed: {result.Error}"));

        State = MergeStepResult(State, stepDef.Name, result);
        return (result, null);
    }

    private async Task<FlowResult?> ExecuteBranchStepIfNeeded(FlowStepResult result, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(result.NextStep))
            return null;

        var nextStep = _definition.Steps.FirstOrDefault(s =>
            s.Id.ToString() == result.NextStep || s.Name == result.NextStep);
        if (nextStep == null)
            return null;

        var flowStepNext = _executor.ResolveStep(nextStep);
        var nextResult = await ExecuteStepWithRetryAsync(flowStepNext, nextStep, ct).ConfigureAwait(false);

        if (!nextResult.Success)
            return FlowResult.CreateFailure($"Step '{nextStep.Name}' failed: {nextResult.Error}");

        State = MergeStepResult(State, nextStep.Name, nextResult);
        return new FlowResult { Success = true, Output = nextResult.Output, OutputState = State };
    }

    private async Task<FlowResult> ExecuteLoopAsync(CancellationToken ct)
    {
        var maxIterations = _definition.Configuration.MaxRetries > 0
            ? _definition.Configuration.MaxRetries
            : 100;

        // Check if configuration has a specific max_iterations setting
        var configMaxIter = _definition.Configuration.Settings.Get<int>("max_iterations");
        if (configMaxIter > 0)
            maxIterations = configMaxIter;

        object? lastOutput = null;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();

            State = State.Set("iteration_count", iteration);

            foreach (var stepDef in _definition.Steps)
            {
                ct.ThrowIfCancellationRequested();

                var flowStep = _executor.ResolveStep(stepDef);
                var result = await ExecuteStepWithRetryAsync(flowStep, stepDef, ct).ConfigureAwait(false);

                if (!result.Success)
                {
                    return FlowResult.CreateFailure($"Step '{stepDef.Name}' failed on iteration {iteration}: {result.Error}");
                }

                State = MergeStepResult(State, stepDef.Name, result);
                lastOutput = result.Output;
            }

            // Check exit condition
            var exitLoop = State.Get<object>("_exit_loop");
            if (exitLoop is true or "true")
            {
                break;
            }
        }

        return new FlowResult
        {
            Success = true,
            Output = lastOutput,
            OutputState = State
        };
    }

    private async Task<FlowStepResult> ExecuteStepWithRetryAsync(
        IFlowStep flowStep,
        FlowStep stepDef,
        CancellationToken ct)
    {
        var maxRetries = stepDef.CanRetry ? stepDef.MaxRetries : 0;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                FlowStepResult result;

                if (stepDef.Timeout.HasValue)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutCts.CancelAfter(stepDef.Timeout.Value);
                    result = await flowStep.ExecuteAsync(State, timeoutCts.Token).ConfigureAwait(false);
                }
                else
                {
                    result = await flowStep.ExecuteAsync(State, ct).ConfigureAwait(false);
                }

                if (result.Success || attempt == maxRetries)
                    return result;

                LogStepFailed(stepDef.Name, attempt + 1, maxRetries + 1, result.Error);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Step timeout
                if (attempt == maxRetries)
                    return FlowStepResult.CreateFailure($"Step '{stepDef.Name}' timed out.");

                LogStepTimedOut(stepDef.Name, attempt + 1, maxRetries + 1);
            }

            // Exponential backoff
            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        return FlowStepResult.CreateFailure($"Step '{stepDef.Name}' failed after {maxRetries + 1} attempts.");
    }

    private static FlowState MergeStepResult(FlowState state, string stepName, FlowStepResult result)
    {
        // Store step output under the step name
        if (result.Output != null)
        {
            state = state.Set($"{stepName}.output", result.Output);
        }

        // Merge the updated context from the step result
        if (result.UpdatedContext != FlowState.Empty && result.UpdatedContext.Count > 0)
        {
            state = state.Merge(result.UpdatedContext.ToDictionary());
        }

        return state;
    }

    /// <summary>
    /// Performs topological sort using Kahn's algorithm.
    /// Groups independent steps together for parallel execution.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<FlowStep>> TopologicalSort(IReadOnlyList<FlowStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var idToStep = steps.ToDictionary(s => s.Id);
        var inDegree = steps.ToDictionary(s => s.Id, _ => 0);
        var adjacency = steps.ToDictionary(s => s.Id, _ => new List<FlowStepId>());

        // Build the graph: if step B depends on A, then A -> B
        foreach (var step in steps)
        {
            foreach (var dep in step.Dependencies)
            {
                if (adjacency.TryGetValue(dep, out var depList))
                {
                    depList.Add(step.Id);
                    inDegree[step.Id]++;
                }
            }
        }

        var groups = new List<IReadOnlyList<FlowStep>>();
        var remaining = new HashSet<FlowStepId>(steps.Select(s => s.Id));

        while (remaining.Count > 0)
        {
            // Find all nodes with in-degree 0
            var readyIds = remaining
                .Where(id => inDegree[id] == 0)
                .ToList();

            if (readyIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Circular dependency detected during topological sort. " +
                    $"Remaining steps: {string.Join(", ", remaining)}");
            }

            var group = readyIds
                .Select(id => idToStep[id])
                .ToList();
            groups.Add(group);

            foreach (var id in readyIds)
            {
                remaining.Remove(id);
                foreach (var neighbor in adjacency[id])
                {
                    inDegree[neighbor]--;
                }
            }
        }

        return groups;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Flow '{FlowName}' failed with exception")]
    private partial void LogFlowFailed(Exception ex, string flowName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Step '{StepName}' failed on attempt {Attempt}/{MaxRetries}: {Error}")]
    private partial void LogStepFailed(string stepName, int attempt, int maxRetries, string? error);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Step '{StepName}' timed out on attempt {Attempt}/{MaxRetries}")]
    private partial void LogStepTimedOut(string stepName, int attempt, int maxRetries);
}
