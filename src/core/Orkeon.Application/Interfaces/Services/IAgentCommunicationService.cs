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
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SendMessageAsync(AgentMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives messages asynchronously for a specific agent.
    /// </summary>
    /// <param name="agentId">The agent whose messages are read.</param>
    /// <param name="cancellationToken">Cancellation token; ends the enumeration.</param>
    IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an agent is available for communication.
    /// </summary>
    System.Threading.Tasks.Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken cancellationToken = default);
}
