using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.AgentCommunication;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Minimal mock of <see cref="IAgentCommunicationService"/> for unit tests.
/// </summary>
public sealed class MockAgentCommunicationService : IAgentCommunicationService
{
    public ValueTask SendMessageAsync(AgentMessage message) => ValueTask.CompletedTask;

    public async IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
