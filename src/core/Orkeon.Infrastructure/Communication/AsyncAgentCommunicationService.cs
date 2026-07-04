using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.AgentCommunication;

namespace Orkeon.Infrastructure.Communication;

/// <summary>
/// Asynchronous agent communication service using System.Threading.Channels.
/// </summary>
public sealed class AsyncAgentCommunicationService : IAgentCommunicationService, IDisposable
{
    private readonly Dictionary<AgentId, Channel<AgentMessage>> _agentChannels = [];
    private readonly Dictionary<AgentId, bool> _agentAvailability = [];
    private readonly SemaphoreSlim _channelsLock = new(1, 1);
    private readonly SemaphoreSlim _availabilityLock = new(1, 1);

    /// <summary>
    /// Sends a message asynchronously to the target agent's channel.
    /// </summary>
    public ValueTask SendMessageAsync(AgentMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SendMessageCoreAsync(message);

        async ValueTask SendMessageCoreAsync(AgentMessage message)
        {
            await _channelsLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // Ensure the target agent has a channel
                if (!_agentChannels.TryGetValue(message.To, out var targetChannel))
                {
                    targetChannel = Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
                    {
                        SingleReader = false,
                        SingleWriter = false
                    });
                    _agentChannels[message.To] = targetChannel;
                }

                // Write the message to the target agent's channel
                await targetChannel.Writer.WriteAsync(message).ConfigureAwait(false);
            }
            finally
            {
                _channelsLock.Release();
            }
        }
    }

    /// <summary>
    /// Receives messages asynchronously for a specific agent.
    /// </summary>
    public IAsyncEnumerable<AgentMessage> ReceiveMessagesAsync(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        return ReceiveMessagesAsyncCore(agentId);
    }

    private async IAsyncEnumerable<AgentMessage> ReceiveMessagesAsyncCore(
        AgentId agentId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<AgentMessage>? channel;
        await _channelsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Ensure the agent has a channel
            if (!_agentChannels.TryGetValue(agentId, out channel))
            {
                channel = Channel.CreateUnbounded<AgentMessage>(new UnboundedChannelOptions
                {
                    SingleReader = false,
                    SingleWriter = false
                });
                _agentChannels[agentId] = channel;
            }
        }
        finally
        {
            _channelsLock.Release();
        }

        // Read with cancellation support
        while (!cancellationToken.IsCancellationRequested)
        {
            bool canRead;
            try
            {
                canRead = await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation requested
                break;
            }

            if (canRead)
            {
                while (channel.Reader.TryRead(out var message))
                {
                    yield return message;
                }
            }
            else
            {
                // Channel completed
                break;
            }
        }
    }

    /// <summary>
    /// Checks if an agent is available for communication.
    /// </summary>
    public Task<bool> IsAgentAvailableAsync(AgentId agentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        return IsAgentAvailableCoreAsync(agentId, cancellationToken);

        async Task<bool> IsAgentAvailableCoreAsync(AgentId agentId, CancellationToken cancellationToken)
        {
            await _availabilityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return _agentAvailability.TryGetValue(agentId, out var available) && available;
            }
            finally
            {
                _availabilityLock.Release();
            }
        }
    }

    /// <summary>
    /// Sets the availability status of an agent.
    /// </summary>
    public Task SetAgentAvailabilityAsync(AgentId agentId, bool isAvailable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        return SetAgentAvailabilityCoreAsync(agentId, isAvailable, cancellationToken);

        async Task SetAgentAvailabilityCoreAsync(AgentId agentId, bool isAvailable, CancellationToken cancellationToken)
        {
            await _availabilityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _agentAvailability[agentId] = isAvailable;
            }
            finally
            {
                _availabilityLock.Release();
            }
        }
    }

    /// <summary>Releases the semaphores guarding the channel and availability maps.</summary>
    public void Dispose()
    {
        _channelsLock.Dispose();
        _availabilityLock.Dispose();
    }
}
