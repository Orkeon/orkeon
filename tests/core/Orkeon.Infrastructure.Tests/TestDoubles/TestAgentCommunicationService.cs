using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;
namespace Orkeon.Infrastructure.Tests.TestDoubles;

public class TestAgentCommunicationService : IAgentCommunicationService
{
    private readonly List<AgentMessage> _sentMessages = [];
    private readonly Dictionary<AgentId, List<AgentMessage>> _agentMessages = [];
    private readonly Dictionary<AgentId, bool> _agentAvailability = [];
    private Exception? _exceptionToThrow;

    public IReadOnlyList<AgentMessage> SentMessages => _sentMessages;

    public void SetupAgentAvailability(AgentId agentId, bool isAvailable)
    {
        _agentAvailability[agentId] = isAvailable;
    }

    public void SetupMessagesFor(AgentId agentId, params AgentMessage[] messages)
    {
        _agentMessages[agentId] = messages.ToList();
    }

    public void SetupThrowException(Exception exception)
    {
        _exceptionToThrow = exception;
    }

    public ValueTask SendMessageAsync(AgentMessage message)
    {
        if (_exceptionToThrow != null)
        {
            throw _exceptionToThrow;
        }

        _sentMessages.Add(message);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId)
    {
        if (_agentMessages.TryGetValue(agentId, out var messages))
        {
            foreach (var message in messages)
            {
                await Task.Yield();
                yield return message;
            }
        }
    }

    public Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_agentAvailability.GetValueOrDefault(agentId, true));
    }

    public AgentMessage? GetLastSentMessage()
    {
        return _sentMessages.LastOrDefault();
    }

    public bool HasSentMessageTo(AgentId agentId)
    {
        return _sentMessages.Any(m => m.To == agentId);
    }
}
