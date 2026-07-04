
using Orkeon.Domain.Common;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Manages agent lifecycle: registration, graceful stop, and emergency kill.
/// </summary>
public interface IAgentLifecycleManager
{
    /// <summary>Registers an agent for lifecycle management.</summary>
    void Register(AgentId agentId, CancellationTokenSource cts);

    /// <summary>Gracefully stops an agent (allows current operation to finish).</summary>
    System.Threading.Tasks.Task StopGracefulAsync(AgentId agentId, TimeSpan? timeout = null);

    /// <summary>Immediately kills an agent by cancelling its token.</summary>
    void Kill(AgentId agentId, string reason);

    /// <summary>Kills all registered agents.</summary>
    void KillAll(string reason);

    /// <summary>Gets the lifecycle state of an agent.</summary>
    AgentLifecycleState GetState(AgentId agentId);

    /// <summary>Checks if an agent is registered.</summary>
    bool IsRegistered(AgentId agentId);
}

/// <summary>
/// Represents the lifecycle state of an agent.
/// </summary>
public enum AgentLifecycleState
{
    /// <summary>Unknown.</summary>
    Unknown,
    /// <summary>Registered.</summary>
    Registered,
    /// <summary>Running.</summary>
    Running,
    /// <summary>Stop Requested.</summary>
    StopRequested,
    /// <summary>Killed.</summary>
    Killed
}
