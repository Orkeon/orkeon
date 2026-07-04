using System.Collections.Concurrent;

namespace Orkeon.Infrastructure.Flows.Visualization;

/// <summary>
/// Tracks the execution state of running flows, providing per-step state updates.
/// </summary>
public class FlowExecutionTracker
{
    private readonly ConcurrentDictionary<string, FlowExecutionState> _states = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, StepState>> _stepStates = new();

    /// <summary>
    /// Starts tracking a new flow execution.
    /// </summary>
    /// <param name="flowId">The unique flow execution identifier.</param>
    /// <returns>The initial execution state.</returns>
    public FlowExecutionState StartTracking(string flowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        var stepStates = new ConcurrentDictionary<string, StepState>();
        _stepStates[flowId] = stepStates;

        var state = new FlowExecutionState(flowId, stepStates, DateTime.UtcNow);
        _states[flowId] = state;
        return state;
    }

    /// <summary>
    /// Updates the state of a specific step within a tracked flow.
    /// </summary>
    /// <param name="flowId">The flow execution identifier.</param>
    /// <param name="stepId">The step identifier to update.</param>
    /// <param name="state">The new step state.</param>
    /// <exception cref="InvalidOperationException">Thrown when the flow is not being tracked.</exception>
    public void UpdateStepState(string flowId, string stepId, StepState state)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        if (!_stepStates.TryGetValue(flowId, out var stepStates))
            throw new InvalidOperationException($"Flow '{flowId}' is not being tracked. Call StartTracking first.");

        stepStates[stepId] = state;

        // Update the stored state
        _states[flowId] = _states[flowId] with
        {
            StepStates = stepStates
        };
    }

    /// <summary>
    /// Marks a tracked flow as complete.
    /// </summary>
    /// <param name="flowId">The flow execution identifier.</param>
    public void CompleteTracking(string flowId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flowId);

        if (_states.TryGetValue(flowId, out var state))
        {
            _states[flowId] = state with { CompletedAt = DateTime.UtcNow };
        }
    }

    /// <summary>
    /// Gets the current execution state for a tracked flow.
    /// </summary>
    /// <param name="flowId">The flow execution identifier.</param>
    /// <returns>The execution state, or null if the flow is not being tracked.</returns>
    public FlowExecutionState? GetExecutionState(string flowId)
    {
        _states.TryGetValue(flowId, out var state);
        return state;
    }
}
