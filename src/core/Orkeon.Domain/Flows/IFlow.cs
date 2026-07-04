using Orkeon.Domain.Common;
using Orkeon.Domain.Flows.ValueObjects;

namespace Orkeon.Domain.Flows;

/// <summary>
/// Base interface for all flow implementations.
/// </summary>
public interface IFlow
{
    /// <summary>
    /// Gets the flow identifier.
    /// </summary>
    FlowId Id { get; }

    /// <summary>
    /// Gets the flow name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the flow state.
    /// </summary>
    FlowState State { get; }

    /// <summary>
    /// Executes the flow.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The flow execution result.</returns>
    Task<FlowResult> ExecuteAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a flow execution.
/// </summary>
public sealed record FlowResult
{
    /// <summary>Gets whether the flow succeeded.</summary>
    public bool Success { get; init; }
    /// <summary>Gets the output produced by the flow.</summary>
    public object? Output { get; init; }
    /// <summary>Gets the error message if the flow failed.</summary>
    public string? Error { get; init; }
    /// <summary>Gets the output state after execution.</summary>
    public FlowState OutputState { get; init; } = FlowState.Empty;

    /// <summary>Creates a successful flow result.</summary>
    /// <param name="output">The optional output.</param>
    /// <returns>A successful <see cref="FlowResult"/>.</returns>
    public static FlowResult CreateSuccess(object? output = null)
    {
        return new FlowResult { Success = true, Output = output };
    }

    /// <summary>Creates a failed flow result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed <see cref="FlowResult"/>.</returns>
    public static FlowResult CreateFailure(string error)
    {
        return new FlowResult { Success = false, Error = error };
    }
}
