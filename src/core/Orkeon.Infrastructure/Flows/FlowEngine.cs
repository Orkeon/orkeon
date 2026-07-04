using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Flows;
using Orkeon.Domain.Flows.ValueObjects;

namespace Orkeon.Infrastructure.Flows;

/// <summary>
/// Core flow execution engine that implements IFlowEngine.
/// </summary>
public partial class FlowEngine : IFlowEngine
{
    private static readonly string[] s_knownStepTypes = ["crew", "llm", "tool", "conditional", "human_input", "delay"];

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FlowEngine> _logger;
    private readonly ConcurrentDictionary<string, IFlowDefinition> _registeredFlows = new();
    private readonly ConcurrentBag<FlowExecutionResult> _executionHistory = [];
    private readonly ConcurrentDictionary<string, StepExecutionCounter> _stepCounters = new();

    /// <summary>Initializes a new instance of <see cref="FlowEngine"/>.</summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="logger">The logger.</param>
    public FlowEngine(IServiceProvider serviceProvider, ILogger<FlowEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Registers a flow definition for later execution and metrics tracking.
    /// </summary>
    public void RegisterFlow(IFlowDefinition flowDefinition)
    {
        ArgumentNullException.ThrowIfNull(flowDefinition);
        _registeredFlows[flowDefinition.Name] = flowDefinition;
    }

    /// <inheritdoc />
    public Task<FlowResult> ExecuteFlowAsync(IFlow flow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flow);
        return ExecuteFlowCoreAsync(flow, cancellationToken);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Flow-engine fault barrier: any failure inside a flow is logged and converted into a failed FlowResult so one flow cannot crash the engine (cancellation is handled separately).")]
    private async Task<FlowResult> ExecuteFlowCoreAsync(IFlow flow, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var startedAt = DateTime.UtcNow;

        LogStartingFlowExecutionId(flow.Name, flow.Id);

        FlowResult result;
        try
        {
            result = await flow.ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result = FlowResult.CreateFailure("Flow execution was cancelled.");
        }
        catch (Exception ex)
        {
            LogFlowFailedWithException(ex, flow.Name);
            result = FlowResult.CreateFailure($"Flow execution failed: {ex.Message}");
        }

        sw.Stop();

        var executionResult = new FlowExecutionResult
        {
            FlowId = flow.Id,
            Success = result.Success,
            Output = result.Output,
            Error = result.Error,
            Duration = sw.Elapsed,
            OutputState = result.OutputState.ToDictionary(),
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow
        };

        _executionHistory.Add(executionResult);

        LogFlowCompletedInMsSuccess(flow.Name, sw.ElapsedMilliseconds, result.Success);

        return result;
    }

    /// <inheritdoc />
    public Task<FlowStepResult> ExecuteStepAsync(
        IFlowStep flowStep,
        Dictionary<string, object> context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(flowStep);
        context ??= [];
        return ExecuteStepCoreAsync(flowStep, context, cancellationToken);
    }

    private async Task<FlowStepResult> ExecuteStepCoreAsync(
        IFlowStep step,
        Dictionary<string, object> context,
        CancellationToken cancellationToken)
    {
        var flowState = FlowState.FromDictionary(context);

        LogExecutingStep(step.Name);

        var sw = Stopwatch.StartNew();
        var result = await step.ExecuteAsync(flowState, cancellationToken).ConfigureAwait(false);
        sw.Stop();

        // Track step execution counts
        _stepCounters.AddOrUpdate(
            step.Name,
            _ => new StepExecutionCounter { Count = 1 },
            (_, counter) => { counter.Count++; return counter; });

        LogStepCompletedInMsSuccess(step.Name, sw.ElapsedMilliseconds, result.Success);

        return result;
    }

    /// <summary>
    /// Executes a registered flow definition by creating a DefinitionBasedFlow.
    /// </summary>
    public Task<FlowResult> ExecuteDefinitionAsync(
        IFlowDefinition definition,
        FlowState? initialState = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return ExecuteDefinitionCoreAsync(definition, initialState, cancellationToken);
    }

    private async Task<FlowResult> ExecuteDefinitionCoreAsync(
        IFlowDefinition definition,
        FlowState? initialState,
        CancellationToken cancellationToken)
    {
        var executor = new FlowStepExecutor(_serviceProvider);
        var flow = new DefinitionBasedFlow(definition, executor, _logger);

        if (initialState != null)
            flow.State = initialState;

        return await ExecuteFlowAsync(flow, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, IFlowDefinition> GetRegisteredFlows()
    {
        return _registeredFlows.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <inheritdoc />
    public IReadOnlyList<FlowExecutionResult> GetExecutionHistory(int limit = 100)
    {
        return _executionHistory
            .OrderByDescending(r => r.CompletedAt)
            .Take(limit)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public FlowValidationResult ValidateFlow(IFlowDefinition flowDefinition)
    {
        ArgumentNullException.ThrowIfNull(flowDefinition);

        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateFlowBasics(flowDefinition, errors);
        ValidateStepDefinitions(flowDefinition, errors, warnings);
        ValidateDependencyReferences(flowDefinition, errors);
        ValidateWithDefinition(flowDefinition, errors);

        if (errors.Count > 0)
            return new FlowValidationResult(false, errors.AsReadOnly(), warnings.AsReadOnly());

        if (warnings.Count > 0)
            return new FlowValidationResult(true, errors.AsReadOnly(), warnings.AsReadOnly());

        return FlowValidationResult.Valid();
    }

    private static void ValidateFlowBasics(IFlowDefinition flowDefinition, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(flowDefinition.Name))
            errors.Add("Flow name is required.");

        if (flowDefinition.Steps.Count == 0)
            errors.Add("Flow must have at least one step.");
    }

    private static void ValidateStepDefinitions(
        IFlowDefinition flowDefinition, List<string> errors, List<string> warnings)
    {
        var stepIds = new HashSet<string>();
        foreach (var step in flowDefinition.Steps)
        {
            if (string.IsNullOrWhiteSpace(step.Name))
                errors.Add($"Step with ID '{step.Id}' must have a name.");

            if (string.IsNullOrWhiteSpace(step.Type))
                errors.Add($"Step '{step.Name}' must have a type.");

            if (!stepIds.Add(step.Id))
                errors.Add($"Duplicate step ID: '{step.Id}'.");

            if (!string.IsNullOrEmpty(step.Type) &&
                !s_knownStepTypes.Contains(step.Type, StringComparer.OrdinalIgnoreCase))
            {
                warnings.Add($"Step '{step.Name}' has unknown type '{step.Type}'.");
            }
        }
    }

    private static void ValidateDependencyReferences(IFlowDefinition flowDefinition, List<string> errors)
    {
        var allIds = flowDefinition.Steps.Select(s => s.Id).ToHashSet();
        foreach (var step in flowDefinition.Steps)
        {
            foreach (var dep in step.Dependencies)
            {
                if (!allIds.Contains(dep))
                    errors.Add($"Step '{step.Name}' depends on unknown step '{dep}'.");
            }
        }
    }

    private static void ValidateWithDefinition(IFlowDefinition flowDefinition, List<string> errors)
    {
        if (flowDefinition.Validate(out var defErrors))
            return;

        foreach (var err in defErrors)
        {
            if (!errors.Contains(err))
                errors.Add(err);
        }
    }

    /// <inheritdoc />
    public FlowMetrics GetFlowMetrics(string? flowName = null)
    {
        var relevant = flowName != null
            ? _executionHistory.Where(r => r.FlowId == flowName).ToList()
            : _executionHistory.ToList();

        var total = relevant.Count;
        var successful = relevant.Count(r => r.Success);
        var failed = total - successful;
        var avgTime = total > 0
            ? TimeSpan.FromMilliseconds(relevant.Average(r => r.Duration.TotalMilliseconds))
            : TimeSpan.Zero;
        var lastExecution = relevant.Count > 0
            ? relevant.Max(r => r.CompletedAt)
            : (DateTime?)null;

        var stepCounts = _stepCounters.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count);

        return new FlowMetrics(
            FlowName: flowName,
            TotalExecutions: total,
            SuccessfulExecutions: successful,
            FailedExecutions: failed,
            AverageExecutionTime: avgTime,
            LastExecution: lastExecution,
            StepExecutionCounts: stepCounts);
    }

    private sealed class StepExecutionCounter
    {
        public int Count { get; set; }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Starting flow execution: '{FlowName}' (ID: {FlowId})")]
    private partial void LogStartingFlowExecutionId(string flowName, string flowId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Flow '{FlowName}' failed with exception")]
    private partial void LogFlowFailedWithException(Exception ex, string flowName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Flow '{FlowName}' completed in {Duration}ms. Success: {Success}")]
    private partial void LogFlowCompletedInMsSuccess(string flowName, long duration, bool success);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Executing step: '{StepName}'")]
    private partial void LogExecutingStep(string stepName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Step '{StepName}' completed in {Duration}ms. Success: {Success}")]
    private partial void LogStepCompletedInMsSuccess(string stepName, long duration, bool success);

}
