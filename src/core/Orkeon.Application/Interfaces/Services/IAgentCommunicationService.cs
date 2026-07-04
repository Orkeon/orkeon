using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;

namespace Orkeon.Application.Interfaces.Services;

/// <summary>
/// Interface for agent communication services with async message passing.
/// </summary>
public interface IAgentCommunicationService
{
    /// <summary>
    /// Sends a message asynchronously using the structured AgentMessage type.
    /// </summary>
    ValueTask SendMessageAsync(AgentMessage message);

    /// <summary>
    /// Receives messages asynchronously for a specific agent.
    /// </summary>
    IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId);

    /// <summary>
    /// Checks if an agent is available for communication.
    /// </summary>
    System.Threading.Tasks.Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken cancellationToken = default);
}
