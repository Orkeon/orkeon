using Orkeon.Domain.Flows.ValueObjects;

namespace Orkeon.Domain.Flows;

/// <summary>
/// Interface for a single step in a flow.
/// </summary>
public interface IFlowStep
{
    /// <summary>
    /// Gets the step name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the step description.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the step.
    /// </summary>
    /// <param name="context">The current flow state context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The step execution result.</returns>
    Task<FlowStepResult> ExecuteAsync(FlowState context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a flow step execution.
/// </summary>
public class FlowStepResult
{
    /// <summary>Gets whether the step succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the output from the step.</summary>
    public object? Output { get; init; }
    /// <summary>Gets the error message if the step failed.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the updated context after step execution.</summary>
    public FlowState UpdatedContext { get; init; } = FlowState.Empty;
    /// <summary>Gets the name of the next step to execute, or <see langword="null"/> to use default.</summary>
    public string? NextStep { get; init; }

    /// <summary>Creates a successful step result.</summary>
    /// <param name="output">The optional output.</param>
    /// <param name="nextStep">The optional next step name.</param>
    /// <returns>A successful <see cref="FlowStepResult"/>.</returns>
    public static FlowStepResult CreateSuccess(object? output = null, string? nextStep = null)
    {
        return new FlowStepResult
        {
            Success = true,
            Output = output,
            NextStep = nextStep
        };
    }

    /// <summary>Creates a failed step result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed <see cref="FlowStepResult"/>.</returns>
    public static FlowStepResult CreateFailure(string error)
    {
        return new FlowStepResult { Success = false, Error = error };
    }
}
