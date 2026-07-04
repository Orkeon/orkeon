using Orkeon.Domain.Task;

namespace Orkeon.Domain.Agent;

/// <summary>
/// Interface for callbacks during agent step execution.
/// </summary>
public interface IStepCallback
{
    /// <summary>
    /// Called when a step is about to start.
    /// </summary>
    System.Threading.Tasks.Task OnStepStartAsync(Agent agent, ICrewTask task, int iteration);

    /// <summary>
    /// Called when a step completes successfully.
    /// </summary>
    System.Threading.Tasks.Task OnStepCompletedAsync(Agent agent, ICrewTask task, int iteration, AgentStep agentStep);

    /// <summary>
    /// Called when a step fails.
    /// </summary>
    System.Threading.Tasks.Task OnStepFailedAsync(Agent agent, ICrewTask task, int iteration, string errorMessage);
}
