using Orkeon.Domain.Common;

namespace Orkeon.Domain.Flows;

/// <summary>
/// Result of a flow execution.
/// </summary>
public record FlowExecutionResult
{
    /// <summary>Gets the flow identifier.</summary>
    public FlowId FlowId { get; init; } = FlowId.Create();
    /// <summary>Gets whether the flow succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the output produced by the flow.</summary>
    public object? Output { get; init; }
    /// <summary>Gets the error message if the flow failed.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the total execution duration.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Gets the output state after execution.</summary>
    public Dictionary<string, object> OutputState { get; init; } = [];
    /// <summary>Gets the per-step execution details.</summary>
    public IReadOnlyList<FlowStepExecutionDetail> StepResults { get; init; } = Array.Empty<FlowStepExecutionDetail>();
    /// <summary>Gets when the flow started.</summary>
    public DateTime StartedAt { get; init; }
    /// <summary>Gets when the flow completed.</summary>
    public DateTime CompletedAt { get; init; }

    /// <summary>Creates a successful flow execution result.</summary>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="output">The optional output.</param>
    /// <returns>A successful <see cref="FlowExecutionResult"/>.</returns>
    public static FlowExecutionResult CreateSuccess(FlowId flowId, object? output = null)
    {
        return new FlowExecutionResult
        {
            FlowId = flowId,
            Success = true,
            Output = output,
            CompletedAt = DateTime.UtcNow
        };
    }

    /// <summary>Creates a failed flow execution result.</summary>
    /// <param name="flowId">The flow identifier.</param>
    /// <param name="error">The error message.</param>
    /// <returns>A failed <see cref="FlowExecutionResult"/>.</returns>
    public static FlowExecutionResult CreateFailure(FlowId flowId, string error)
    {
        return new FlowExecutionResult
        {
            FlowId = flowId,
            Success = false,
            Error = error,
            CompletedAt = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Detailed result of a flow step execution with metadata.
/// </summary>
public sealed record FlowStepExecutionDetail
{
    /// <summary>Gets the step identifier.</summary>
    public FlowStepId StepId { get; init; } = FlowStepId.Create();
    /// <summary>Gets the step name.</summary>
    public string StepName { get; init; } = string.Empty;
    /// <summary>Gets whether the step succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the output from the step.</summary>
    public object? Output { get; init; }
    /// <summary>Gets the error message if the step failed.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the step execution duration.</summary>
    public TimeSpan Duration { get; init; }
    /// <summary>Gets when the step was executed.</summary>
    public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for flow completion.
/// </summary>
public class FlowCompletedEventArgs : EventArgs
{
    /// <summary>Gets the flow execution result.</summary>
    public FlowExecutionResult Result { get; }

    /// <summary>Initializes a new instance of <see cref="FlowCompletedEventArgs"/>.</summary>
    /// <param name="result">The flow execution result.</param>
    public FlowCompletedEventArgs(FlowExecutionResult result)
    {
        Result = result;
    }
}

/// <summary>
/// Result specific to event-driven flows.
/// </summary>
public record EventDrivenFlowResult : FlowExecutionResult
{
    /// <summary>Gets the list of events processed during execution.</summary>
    public IReadOnlyList<FlowEvent> Events { get; init; } = Array.Empty<FlowEvent>();
    /// <summary>Gets the event type counts.</summary>
    public Dictionary<string, int> EventCounts { get; init; } = [];
    /// <summary>Gets the total number of events processed.</summary>
    public int TotalEventsProcessed { get; init; }
}
