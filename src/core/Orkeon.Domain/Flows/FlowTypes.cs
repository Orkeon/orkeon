using Orkeon.Domain.Common;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Flows;

/// <summary>
/// Types of flows supported in the system.
/// </summary>
public enum FlowType
{
    /// <summary>Steps execute one after another.</summary>
    Sequential,
    /// <summary>Steps execute in parallel.</summary>
    Parallel,
    /// <summary>Steps execute based on conditions.</summary>
    Conditional,
    /// <summary>Steps execute in a loop.</summary>
    Loop,
    /// <summary>Flow delegates to a crew.</summary>
    Crew,
    /// <summary>Custom flow type.</summary>
    Custom
}

/// <summary>
/// Status of a flow execution.
/// </summary>
public enum FlowStatus
{
    /// <summary>The flow has not started.</summary>
    NotStarted,
    /// <summary>The flow is running.</summary>
    Running,
    /// <summary>The flow is suspended.</summary>
    Suspended,
    /// <summary>The flow completed successfully.</summary>
    Completed,
    /// <summary>The flow failed.</summary>
    Failed,
    /// <summary>The flow was cancelled.</summary>
    Cancelled
}

/// <summary>
/// Configuration for a flow.
/// </summary>
public sealed record FlowConfiguration
{
    /// <summary>Gets the unique identifier.</summary>
    public FlowId Id { get; init; } = FlowId.Create();
    /// <summary>Gets the flow name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the flow type.</summary>
    public FlowType Type { get; init; } = FlowType.Sequential;
    /// <summary>Gets the flow configuration settings.</summary>
    public FlowConfigurationSettings Settings { get; init; } = FlowConfigurationSettings.Empty;
    /// <summary>Gets the optional timeout.</summary>
    public TimeSpan? Timeout { get; init; }
    /// <summary>Gets the maximum number of retries.</summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;
    /// <summary>Gets whether logging is enabled.</summary>
    public bool EnableLogging { get; init; } = true;
    /// <summary>Gets whether metrics collection is enabled.</summary>
    public bool EnableMetrics { get; init; } = true;
}

/// <summary>
/// Context for flow execution.
/// </summary>
public class FlowContext
{
    private readonly List<FlowEvent> _events = [];

    /// <summary>Gets or sets the flow identifier.</summary>
    public FlowId FlowId { get; private set; } = FlowId.Create();
    /// <summary>Gets or sets the current flow status.</summary>
    public FlowStatus Status { get; private set; } = FlowStatus.NotStarted;

    /// <summary>
    /// Gets or sets the typed flow state.
    /// </summary>
    public FlowState State { get; private set; } = FlowState.Empty;

    /// <summary>Gets the list of flow events.</summary>
    public IReadOnlyList<FlowEvent> Events => _events;
    /// <summary>Gets when the flow started.</summary>
    public DateTime StartedAt { get; private set; }
    /// <summary>Gets when the flow completed, or <see langword="null"/> if still running.</summary>
    public DateTime? CompletedAt { get; private set; }
    /// <summary>Gets the identifier of the current step.</summary>
    public FlowStepId? CurrentStepId { get; private set; }
    /// <summary>Gets the current retry count.</summary>
    public int RetryCount { get; private set; }
    /// <summary>Gets the last error encountered.</summary>
    public Exception? LastError { get; private set; }

    /// <summary>
    /// Creates a new <see cref="FlowContext"/> with default values.
    /// </summary>
    public FlowContext()
    {
    }

    /// <summary>
    /// Creates a new <see cref="FlowContext"/> with the specified values.
    /// </summary>
    public FlowContext(
        FlowId flowId,
        FlowStatus status = FlowStatus.NotStarted,
        DateTime startedAt = default,
        DateTime? completedAt = null,
        FlowStepId? currentStepId = null,
        int retryCount = 0,
        Exception? lastError = null)
    {
        FlowId = flowId;
        Status = status;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        CurrentStepId = currentStepId;
        RetryCount = retryCount;
        LastError = lastError;
    }

    /// <summary>Updates the flow identifier.</summary>
    public void UpdateFlowId(FlowId flowId) => FlowId = flowId;

    /// <summary>Updates the flow status.</summary>
    public void UpdateStatus(FlowStatus status) => Status = status;

    /// <summary>Updates the started-at timestamp.</summary>
    public void UpdateStartedAt(DateTime startedAt) => StartedAt = startedAt;

    /// <summary>Updates the completed-at timestamp.</summary>
    public void UpdateCompletedAt(DateTime? completedAt) => CompletedAt = completedAt;

    /// <summary>Updates the current step identifier.</summary>
    public void UpdateCurrentStepId(FlowStepId? currentStepId) => CurrentStepId = currentStepId;

    /// <summary>Updates the retry count.</summary>
    public void UpdateRetryCount(int retryCount) => RetryCount = retryCount;

    /// <summary>Updates the last error.</summary>
    public void UpdateLastError(Exception? lastError) => LastError = lastError;

    /// <summary>Sets a variable in the flow state.</summary>
    /// <param name="key">The variable key.</param>
    /// <param name="value">The variable value.</param>
    public void SetVariable(string key, object value)
    {
        if (value is not null)
        {
            State = State.SetVariable(key, value);
        }
    }

    /// <summary>Gets a variable from the flow state.</summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The variable key.</param>
    /// <returns>The variable value, or <see langword="null"/> if not found.</returns>
    public T? GetVariable<T>(string key) where T : class
    {
        return State.GetVariable<T>(key);
    }

    /// <summary>
    /// Sets a shared state value.
    /// </summary>
    /// <param name="key">The state key.</param>
    /// <param name="value">The value to set.</param>
    public void SetSharedState(string key, object value)
    {
        if (value is not null)
        {
            State = State.SetSharedState(key, value);
        }
    }

    /// <summary>
    /// Gets a shared state value.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The state key.</param>
    /// <returns>The value, or <see langword="null"/> if not found.</returns>
    public T? GetSharedState<T>(string key) where T : class
    {
        return State.GetSharedState<T>(key);
    }

    /// <summary>Records a flow event.</summary>
    /// <param name="flowEvent">The event to record.</param>
    public void RecordEvent(FlowEvent flowEvent)
    {
        _events.Add(flowEvent);
    }

    /// <summary>
    /// Creates a snapshot of the current state.
    /// </summary>
    /// <param name="label">An optional label for the snapshot.</param>
    /// <returns>The state snapshot.</returns>
    public FlowStateSnapshot CreateStateSnapshot(string label = "")
    {
        return State.CreateSnapshot(label);
    }

    /// <summary>
    /// Saves a snapshot of the current state.
    /// </summary>
    /// <param name="label">An optional label for the snapshot.</param>
    public void SaveStateSnapshot(string label = "")
    {
        State = State.SaveSnapshot(label);
    }
}

/// <summary>
/// Represents a step in a flow.
/// </summary>
public sealed record FlowStep
{
    /// <summary>Gets the step identifier.</summary>
    public FlowStepId Id { get; init; } = FlowStepId.Create();
    /// <summary>Gets the step name.</summary>
    public string Name { get; init; } = string.Empty;
    /// <summary>Gets the step type.</summary>
    public string Type { get; init; } = string.Empty;
    /// <summary>Gets the step parameters.</summary>
    public FlowStepParameters Parameters { get; init; } = FlowStepParameters.Empty;
    /// <summary>Gets the step dependency identifiers.</summary>
    public IReadOnlyList<FlowStepId> Dependencies { get; init; } = Array.Empty<FlowStepId>();
    /// <summary>Gets the step timeout.</summary>
    public TimeSpan? Timeout { get; init; }
    /// <summary>Gets whether this step can be retried.</summary>
    public bool CanRetry { get; init; } = true;
    /// <summary>Gets the maximum number of retries.</summary>
    public int MaxRetries { get; init; } = AgentDefaults.MaxRetryLimit;
    /// <summary>Gets the step metadata.</summary>
    public FlowStepMetadata Metadata { get; init; } = FlowStepMetadata.Empty;
}

/// <summary>
/// Event that occurs during flow execution.
/// </summary>
public sealed record FlowEvent
{
    /// <summary>Gets the event identifier.</summary>
    public FlowEventId Id { get; init; } = FlowEventId.Create();
    /// <summary>Gets the flow identifier.</summary>
    public FlowId FlowId { get; init; } = FlowId.Create();
    /// <summary>Gets the step identifier that raised the event.</summary>
    public FlowStepId StepId { get; init; } = FlowStepId.Create();
    /// <summary>Gets the event type name.</summary>
    public string EventType { get; init; } = string.Empty;
    /// <summary>Gets when the event occurred.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>Gets the event data.</summary>
    public FlowEventData Data { get; init; } = FlowEventData.Empty;
    /// <summary>Gets the optional error message associated with the event.</summary>
    public string? Error { get; init; }
}
