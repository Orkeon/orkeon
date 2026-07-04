using Orkeon.Domain.Flows;

namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Unified interface for the flow execution engine.
/// Combines core execution (flow &amp; step) with management operations
/// (registration, history, validation, metrics).
/// </summary>
public interface IFlowEngine
{
    /// <summary>
    /// Executes a flow.
    /// </summary>
    Task<FlowResult> ExecuteFlowAsync(IFlow flow, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a flow step.
    /// </summary>
    Task<FlowStepResult> ExecuteStepAsync(IFlowStep flowStep, Dictionary<string, object> context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all registered flows.
    /// </summary>
    IReadOnlyDictionary<string, IFlowDefinition> GetRegisteredFlows();

    /// <summary>
    /// Gets execution history for flows.
    /// </summary>
    IReadOnlyList<FlowExecutionResult> GetExecutionHistory(int limit = 100);

    /// <summary>
    /// Validates a flow definition without executing it.
    /// </summary>
    FlowValidationResult ValidateFlow(IFlowDefinition flowDefinition);

    /// <summary>
    /// Gets metrics about flow executions.
    /// </summary>
    FlowMetrics GetFlowMetrics(string? flowName = null);
}

/// <summary>
/// Result of flow validation.
/// </summary>
public record FlowValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Valid.
    /// </summary>
    public static FlowValidationResult Valid() => new(true, [], []);

    /// <summary>
    /// Invalid.
    /// </summary>
    public static FlowValidationResult Invalid(params string[] errors) =>
        new(false, errors, []);

    /// <summary>
    /// With Warnings.
    /// </summary>
    public static FlowValidationResult WithWarnings(params string[] warnings) =>
        new(true, [], warnings);
}

/// <summary>
/// Metrics about flow executions.
/// </summary>
public record FlowMetrics(
    string? FlowName,
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    TimeSpan AverageExecutionTime,
    DateTime? LastExecution,
    IReadOnlyDictionary<string, int> StepExecutionCounts)
{
    /// <summary>
    /// Gets or sets the success rate.
    /// </summary>
    public double SuccessRate => TotalExecutions > 0 ? (double)SuccessfulExecutions / TotalExecutions : 0.0;
}
