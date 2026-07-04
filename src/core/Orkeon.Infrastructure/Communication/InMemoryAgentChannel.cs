using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;

namespace Orkeon.Infrastructure.Communication;

/// <summary>
/// In-process, lock-free implementation of <see cref="IAgentChannel"/>.
/// Designed for single-process orchestration; swap for a distributed
/// implementation (e.g. Redis Streams) for multi-host deployments.
/// </summary>
public sealed partial class InMemoryAgentChannel : IAgentChannel
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<AgentId, Func<AgentChannelRequest, CancellationToken, Task<AgentChannelResponse>>> _handlers = new();
    private readonly ConcurrentDictionary<CrewId, ConcurrentBag<AgentId>> _crewAgents = new();
    private readonly ILogger<InMemoryAgentChannel> _logger;

    /// <summary>Initializes a new instance of <see cref="InMemoryAgentChannel"/>.</summary>
    public InMemoryAgentChannel(ILogger<InMemoryAgentChannel> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AgentChannelResponse> RequestAsync(
        AgentChannelRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RequestCoreAsync(request, timeout, cancellationToken);
    }

    private async Task<AgentChannelResponse> RequestCoreAsync(
        AgentChannelRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        if (!_handlers.TryGetValue(request.ToAgentId, out var handler))
        {
            LogHandlerNotFound(request.ToAgentId, request.CorrelationId);
            return AgentChannelResponse.Fail(
                request.CorrelationId,
                request.ToAgentId,
                $"No handler registered for agent {request.ToAgentId}");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        try
        {
            LogRequestSent(request.FromAgentId, request.ToAgentId, request.Intent, request.CorrelationId);
            var response = await handler(request, cts.Token).ConfigureAwait(false);
            LogResponseReceived(request.ToAgentId, request.FromAgentId, response.Success, request.CorrelationId);
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            LogRequestTimedOut(request.FromAgentId, request.ToAgentId, effectiveTimeout, request.CorrelationId);
            throw new TimeoutException(
                $"Agent {request.ToAgentId} did not respond within {effectiveTimeout}.");
        }
    }

    /// <inheritdoc />
    public IDisposable RegisterHandler(
        AgentId agentId,
        Func<AgentChannelRequest, CancellationToken, Task<AgentChannelResponse>> handler)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(handler);

        _handlers[agentId] = handler;
        LogHandlerRegistered(agentId);
        return new HandlerRegistration(this, agentId);
    }

    /// <summary>Registers an agent as part of a crew (for broadcast scoping).</summary>
    public void RegisterCrewMember(CrewId crewId, AgentId agentId)
    {
        var bag = _crewAgents.GetOrAdd(crewId, _ => []);
        bag.Add(agentId);
    }

    /// <inheritdoc />
    public async Task BroadcastAsync(
        AgentId fromAgentId,
        CrewId crewId,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (!_crewAgents.TryGetValue(crewId, out var members))
            return;

        var tasks = new List<Task>();
        foreach (var memberId in members.Distinct())
        {
            if (memberId == fromAgentId) continue;
            if (!_handlers.TryGetValue(memberId, out var handler)) continue;

            var request = AgentChannelRequest.Create(fromAgentId, memberId, "broadcast", content);
            tasks.Add(handler(request, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        LogBroadcastSent(fromAgentId, crewId, tasks.Count);
    }

    private void Unregister(AgentId agentId)
    {
        _handlers.TryRemove(agentId, out _);
        LogHandlerUnregistered(agentId);
    }

    private sealed class HandlerRegistration(InMemoryAgentChannel channel, AgentId agentId) : IDisposable
    {
        public void Dispose() => channel.Unregister(agentId);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request sent: {From} → {To} (intent: {Intent}, correlation: {CorrelationId})")]
    private partial void LogRequestSent(AgentId from, AgentId to, string intent, Guid correlationId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Response received: {From} → {To} (success: {Success}, correlation: {CorrelationId})")]
    private partial void LogResponseReceived(AgentId from, AgentId to, bool success, Guid correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No handler registered for agent {AgentId} (correlation: {CorrelationId})")]
    private partial void LogHandlerNotFound(AgentId agentId, Guid correlationId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request from {From} to {To} timed out after {Timeout} (correlation: {CorrelationId})")]
    private partial void LogRequestTimedOut(AgentId from, AgentId to, TimeSpan timeout, Guid correlationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handler registered for agent {AgentId}")]
    private partial void LogHandlerRegistered(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Handler unregistered for agent {AgentId}")]
    private partial void LogHandlerUnregistered(AgentId agentId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Broadcast from {From} to crew {CrewId}: {RecipientCount} recipients")]
    private partial void LogBroadcastSent(AgentId from, CrewId crewId, int recipientCount);
}
