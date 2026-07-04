using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Http;

namespace Orkeon.Infrastructure.Agent;

/// <summary>
/// Concrete implementation of IAgentLifecycleManager using ConcurrentDictionary.
/// Thread-safe agent lifecycle tracking with immediate kill and graceful stop support.
/// </summary>
public sealed partial class AgentLifecycleManager : IAgentLifecycleManager
{
    private readonly ConcurrentDictionary<string, AgentLifecycleEntry> _entries = new();
    private readonly ILogger<AgentLifecycleManager> _logger;

    /// <summary>Initializes a new instance of <see cref="AgentLifecycleManager"/>.</summary>
    /// <param name="logger">The logger.</param>
    public AgentLifecycleManager(ILogger<AgentLifecycleManager> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Register(AgentId agentId, CancellationTokenSource cts)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(cts);

        var key = agentId.ToString();
        var entry = new AgentLifecycleEntry(cts, AgentLifecycleState.Registered);
        _entries[key] = entry;

        LogAgentRegisteredForLifecycleManagement(key);
    }

    /// <inheritdoc/>
    public Task StopGracefulAsync(AgentId agentId, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        return StopGracefulAsyncCore(agentId, timeout);
    }

    private async Task StopGracefulAsyncCore(AgentId agentId, TimeSpan? timeout)
    {
        var key = agentId.ToString();
        if (!_entries.TryGetValue(key, out var entry))
        {
            LogStopgracefulasyncCalledForUnregisteredAgent(key);
            return;
        }

        entry.State = AgentLifecycleState.StopRequested;
        LogGracefulStopRequestedForAgent(key);

        var effectiveTimeout = timeout ?? HttpDefaults.DefaultHttpTimeout;

        try
        {
            await Task.Delay(effectiveTimeout).ConfigureAwait(false);

            // If still not cancelled, force cancel now
            if (!entry.Cts.IsCancellationRequested)
            {
                await entry.Cts.CancelAsync().ConfigureAwait(false);
                entry.State = AgentLifecycleState.Killed;
                entry.KillReason = "Graceful stop timeout elapsed";
                LogAgentDidNotStopGracefully(key, effectiveTimeout);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal: agent may have been killed by other means during wait
        }
    }

    /// <inheritdoc/>
    public void Kill(AgentId agentId, string reason)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        var key = agentId.ToString();
        if (!_entries.TryGetValue(key, out var entry))
        {
            LogKillCalledForUnregisteredAgent(key, reason);
            return;
        }

        if (!entry.Cts.IsCancellationRequested)
        {
            entry.Cts.Cancel();
        }

        entry.State = AgentLifecycleState.Killed;
        entry.KillReason = reason;

        LogAgentKilledReason(key, reason);
    }

    /// <inheritdoc/>
    public void KillAll(string reason)
    {
        LogKillallInvokedReasonKillingAgents(reason, _entries.Count);

        foreach (var entry in _entries.Select(kvp => kvp.Value))
        {
            if (!entry.Cts.IsCancellationRequested)
            {
                entry.Cts.Cancel();
            }
            entry.State = AgentLifecycleState.Killed;
            entry.KillReason = reason;
        }
    }

    /// <inheritdoc/>
    public AgentLifecycleState GetState(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        var key = agentId.ToString();
        return _entries.TryGetValue(key, out var entry)
            ? entry.State
            : AgentLifecycleState.Unknown;
    }

    /// <inheritdoc/>
    public bool IsRegistered(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        return _entries.ContainsKey(agentId.ToString());
    }

    private sealed class AgentLifecycleEntry
    {
        /// <summary>Gets the cancellation token source for this agent.</summary>
        public CancellationTokenSource Cts { get; }
        /// <summary>Gets or sets the current lifecycle state.</summary>
        public AgentLifecycleState State { get; set; }
        /// <summary>Gets or sets the reason the agent was killed, if applicable.</summary>
        public string? KillReason { get; set; }

        public AgentLifecycleEntry(CancellationTokenSource cts, AgentLifecycleState state)
        {
            Cts = cts;
            State = state;
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Agent {AgentId} registered for lifecycle management")]
    private partial void LogAgentRegisteredForLifecycleManagement(object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "StopGracefulAsync called for unregistered agent {AgentId}")]
    private partial void LogStopgracefulasyncCalledForUnregisteredAgent(object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Graceful stop requested for agent {AgentId}")]
    private partial void LogGracefulStopRequestedForAgent(object agentId);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Agent {AgentId} did not stop gracefully; force-killed after {Timeout}")]
    private partial void LogAgentDidNotStopGracefully(object agentId, object timeout);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Kill called for unregistered agent {AgentId} (reason: {Reason})")]
    private partial void LogKillCalledForUnregisteredAgent(object agentId, object reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Agent {AgentId} killed. Reason: {Reason}")]
    private partial void LogAgentKilledReason(object agentId, object reason);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "KillAll invoked. Reason: {Reason}. Killing {Count} agents.")]
    private partial void LogKillallInvokedReasonKillingAgents(object reason, int count);

}
