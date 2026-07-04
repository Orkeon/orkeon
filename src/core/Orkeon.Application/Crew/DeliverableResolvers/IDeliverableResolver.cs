using Orkeon.Domain.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Application.Crew.DeliverableResolvers;

/// <summary>
/// Contract for a task-level deliverable resolver. Implementations persist the agent's output
/// (or derived form thereof) according to the task's <see cref="TaskDeliverable"/> contract.
/// </summary>
public interface IDeliverableResolver
{
    /// <summary>Which <see cref="DeliverableSource"/> mode this implementation handles.</summary>
    DeliverableSource SupportedSource { get; }

    /// <summary>
    /// Resolve the task's deliverable: transform the agent's final output as needed and persist it.
    /// </summary>
    /// <param name="task">The task carrying the <see cref="TaskDeliverable"/> contract.</param>
    /// <param name="finalAssistantMessage">The final assistant text produced by the agent loop.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="DeliverableResolutionResult"/> describing the outcome.</returns>
    System.Threading.Tasks.Task<DeliverableResolutionResult> ResolveAsync(
        CrewTask task,
        string finalAssistantMessage,
        CancellationToken ct);
}
