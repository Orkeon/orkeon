using Orkeon.Domain.Common;

namespace Orkeon.Domain.Flows;

/// <summary>
/// Event arguments for flow step started events.
/// </summary>
public class FlowStepStartedEventArgs : EventArgs
{
    /// <summary>Gets the step identifier.</summary>
    public FlowStepId StepId { get; }
    /// <summary>Gets the step name.</summary>
    public string StepName { get; }
    /// <summary>Gets when the step started.</summary>
    public DateTime StartedAt { get; }

    /// <summary>Initializes a new instance of <see cref="FlowStepStartedEventArgs"/>.</summary>
    /// <param name="stepId">The step identifier.</param>
    /// <param name="stepName">The step name.</param>
    public FlowStepStartedEventArgs(FlowStepId stepId, string stepName)
    {
        ArgumentNullException.ThrowIfNull(stepId);
        StepId = stepId;
        ArgumentNullException.ThrowIfNull(stepName);
        StepName = stepName;
        StartedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Event arguments for flow step completed events.
/// </summary>
public class FlowStepCompletedEventArgs : EventArgs
{
    /// <summary>Gets the step identifier.</summary>
    public FlowStepId StepId { get; }
    /// <summary>Gets the step name.</summary>
    public string StepName { get; }
    /// <summary>Gets whether the step succeeded.</summary>
    public bool IsSuccess { get; }
    /// <summary>Gets the error message if the step failed.</summary>
    public string? ErrorMessage { get; }
    /// <summary>Gets when the step completed.</summary>
    public DateTime CompletedAt { get; }

    /// <summary>Initializes a new instance of <see cref="FlowStepCompletedEventArgs"/>.</summary>
    /// <param name="stepId">The step identifier.</param>
    /// <param name="stepName">The step name.</param>
    /// <param name="isSuccess">Whether the step succeeded.</param>
    /// <param name="errorMessage">The optional error message.</param>
    public FlowStepCompletedEventArgs(FlowStepId stepId, string stepName, bool isSuccess, string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(stepId);
        StepId = stepId;
        ArgumentNullException.ThrowIfNull(stepName);
        StepName = stepName;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        CompletedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// Context for event-driven flows.
/// </summary>
public class EventDrivenFlowContext
{
    private readonly Dictionary<string, object> _data;
    private readonly List<string> _completedSteps;

    /// <summary>Gets the flow identifier.</summary>
    public FlowId FlowId { get; }
    /// <summary>Gets the flow data dictionary.</summary>
    public IReadOnlyDictionary<string, object> Data => _data;
    /// <summary>Gets the list of completed step names.</summary>
    public IReadOnlyList<string> CompletedSteps => _completedSteps;
    /// <summary>Gets the current step name.</summary>
    public string? CurrentStep { get; private set; }

    /// <summary>Initializes a new instance of <see cref="EventDrivenFlowContext"/>.</summary>
    /// <param name="flowId">The flow identifier.</param>
    public EventDrivenFlowContext(FlowId flowId)
    {
        ArgumentNullException.ThrowIfNull(flowId);
        FlowId = flowId;
        _data = [];
        _completedSteps = [];
    }

    /// <summary>Sets the current step.</summary>
    /// <param name="stepName">The step name, or null if no step is active.</param>
    public void SetCurrentStep(string? stepName) => CurrentStep = stepName;

    /// <summary>Adds a data entry to the flow context.</summary>
    /// <param name="key">The data key.</param>
    /// <param name="value">The data value.</param>
    public void AddData(string key, object value) => _data.Add(key, value);

    /// <summary>Sets a data entry in the flow context, overwriting any existing value.</summary>
    /// <param name="key">The data key.</param>
    /// <param name="value">The data value.</param>
    public void SetData(string key, object value) => _data[key] = value;

    /// <summary>Adds a completed step name.</summary>
    /// <param name="stepName">The completed step name.</param>
    public void AddCompletedStep(string stepName) => _completedSteps.Add(stepName);
}

/// <summary>
/// Flow execution status.
/// </summary>
public enum FlowExecutionStatus
{
    /// <summary>The flow has not started.</summary>
    NotStarted,
    /// <summary>The flow is running.</summary>
    Running,
    /// <summary>The flow completed successfully.</summary>
    Completed,
    /// <summary>The flow failed.</summary>
    Failed,
    /// <summary>The flow is paused.</summary>
    Paused,
    /// <summary>The flow was cancelled.</summary>
    Cancelled
}

/// <summary>
/// Flow listen mode.
/// </summary>
public enum FlowListenMode
{
    /// <summary>Listen at step granularity.</summary>
    Step,
    /// <summary>Listen at flow granularity.</summary>
    Flow,
    /// <summary>Listen to all events.</summary>
    All
}
