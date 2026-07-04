namespace Orkeon.Domain.Agent;

/// <summary>
/// Interface for handlers that support step progress notifications.
/// A no-op stub (<c>NullStepProgressHandler</c>) is registered by default in DI;
/// replace with a real implementation to receive progress events during agent step execution.
/// </summary>
public interface IStepProgressHandler
{
    /// <summary>
    /// Called when a step progresses during task execution.
    /// </summary>
    System.Threading.Tasks.Task OnStepProgressAsync(StepProgressContext context, CancellationToken cancellationToken = default);
}
