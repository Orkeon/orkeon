namespace Orkeon.Application.Interfaces.AgentCommunication;

/// <summary>
/// Routes incoming A2A task requests to the appropriate local agent based on the requested skill.
/// </summary>
public interface IA2ATaskRouter
{
    /// <summary>
    /// Routes a task request to a matching local agent and returns the response.
    /// </summary>
    /// <param name="request">The incoming task request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The task response after execution.</returns>
    System.Threading.Tasks.Task<A2ATaskResponse> RouteTaskAsync(A2ATaskRequest request, CancellationToken ct = default);
}
