using Orkeon.Application.Constants.Execution;
using Orkeon.Domain.Common;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Orkeon.Application.Agent;

/// <summary>
/// Manages dynamically created agents within a crew execution context.
/// Tracks agent lifecycle, parent-child relationships, and ensures cleanup after crew execution.
/// </summary>
public sealed partial class DynamicAgentRegistry
{
    private readonly CrewId _crewId;
    private readonly ILogger<DynamicAgentRegistry>? _logger;
    private readonly ConcurrentDictionary<AgentId, DynamicAgentMetadata> _registry;
    private readonly object _lock = new();
    private bool _isDisposed;

    /// <summary>
    /// Gets the crew ID this registry is scoped to.
    /// </summary>
    public CrewId CrewId => _crewId;

    /// <summary>
    /// Gets the count of currently registered dynamic agents.
    /// </summary>
    public int DynamicAgentCount => _registry.Count;

    /// <summary>
    /// Returns all registered dynamic agents that are not terminated.
    /// </summary>
    public IEnumerable<Domain.Agent.Agent> GetDynamicAgents() =>
        _registry.Values
            .Where(m => !m.IsTerminated)
            .Select(m => m.Agent)
            .ToList();

    /// <summary>
    /// Returns all terminated dynamic agents.
    /// </summary>
    public IEnumerable<Domain.Agent.Agent> GetTerminatedAgents() =>
        _registry.Values
            .Where(m => m.IsTerminated)
            .Select(m => m.Agent)
            .ToList();

    /// <summary>
    /// Creates a new dynamic agent registry for a crew.
    /// </summary>
    public DynamicAgentRegistry(CrewId crewId, ILogger<DynamicAgentRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        _crewId = crewId;
        _logger = logger;
        _registry = new();
    }

    /// <summary>
    /// Registers a dynamically created agent.
    /// </summary>
    /// <param name="agent">The agent to register.</param>
    /// <param name="requestingAgentId">The agent that requested this spawn, if any.</param>
    /// <param name="spawnReason">Optional reason for the spawn.</param>
    public void RegisterAgent(Domain.Agent.Agent agent, AgentId? requestingAgentId = null, string? spawnReason = null)
    {
        ArgumentNullException.ThrowIfNull(agent);

        lock (_lock)
        {
            if (_isDisposed)
                throw new InvalidOperationException("Registry has been disposed.");

            if (_registry.ContainsKey(agent.Id))
                throw new InvalidOperationException($"Agent {agent.Id} is already registered.");

            var metadata = new DynamicAgentMetadata(
                agent,
                requestingAgentId,
                spawnReason,
                DateTime.UtcNow);

            _registry.TryAdd(agent.Id, metadata);

            if (_logger is not null)
                LogAgentRegistered(_logger, agent.Id, _crewId, agent.Role, spawnReason ?? "Unknown");
        }
    }

    /// <summary>
    /// Unregisters and terminates a dynamic agent.
    /// </summary>
    /// <param name="agentId">The agent ID to terminate.</param>
    /// <param name="reason">Optional termination reason.</param>
    /// <returns>True if the agent was terminated, false if not found.</returns>
    public bool TerminateAgent(AgentId agentId, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        lock (_lock)
        {
            if (_registry.TryGetValue(agentId, out var metadata))
            {
                metadata.MarkTerminated(reason);
                if (_logger is not null)
                    LogAgentTerminated(_logger, agentId, _crewId, reason ?? "Unknown");
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Retrieves a registered agent by ID.
    /// </summary>
    public Domain.Agent.Agent? GetAgent(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        lock (_lock)
        {
            return _registry.TryGetValue(agentId, out var metadata) ? metadata.Agent : null;
        }
    }

    /// <summary>
    /// Checks if an agent is registered and not terminated.
    /// </summary>
    public bool IsAgentActive(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        lock (_lock)
        {
            return _registry.TryGetValue(agentId, out var metadata) && !metadata.IsTerminated;
        }
    }

    /// <summary>
    /// Gets metadata about a registered agent.
    /// </summary>
    public DynamicAgentMetadata? GetAgentMetadata(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);

        lock (_lock)
        {
            _registry.TryGetValue(agentId, out var metadata);
            return metadata;
        }
    }

    /// <summary>
    /// Gets all dynamic agents spawned by a specific agent.
    /// </summary>
    public IEnumerable<Domain.Agent.Agent> GetChildrenOf(AgentId parentAgentId)
    {
        ArgumentNullException.ThrowIfNull(parentAgentId);

        lock (_lock)
        {
            return _registry.Values
                .Where(m => m.RequestingAgentId == parentAgentId && !m.IsTerminated)
                .Select(m => m.Agent)
                .ToList();
        }
    }

    /// <summary>
    /// Terminates all active dynamic agents in the registry.
    /// Called at the end of crew execution to clean up.
    /// </summary>
    public void TerminateAllAgents(string reason = StatusDefaults.DefaultCompletionReason)
    {
        lock (_lock)
        {
            var agentsToTerminate = _registry.Values
                .Where(m => !m.IsTerminated)
                .ToList();

            foreach (var metadata in agentsToTerminate)
            {
                metadata.MarkTerminated(reason);
                if (_logger is not null)
                    LogAgentAutoTerminated(_logger, metadata.Agent.Id, _crewId, reason ?? "Crew execution completed");
            }
        }
    }

    /// <summary>
    /// Gets statistics about the registry.
    /// </summary>
    public DynamicAgentRegistryStats GetStats()
    {
        lock (_lock)
        {
            var activeAgents = _registry.Values.Where(m => !m.IsTerminated).ToList();
            var terminatedAgents = _registry.Values.Where(m => m.IsTerminated).ToList();

            return new DynamicAgentRegistryStats(
                TotalAgents: _registry.Count,
                ActiveAgents: activeAgents.Count,
                TerminatedAgents: terminatedAgents.Count,
                AverageAgentLifetime: activeAgents.Count > 0
                    ? TimeSpan.FromMilliseconds(activeAgents.Average(a => a.LifetimeMs))
                    : TimeSpan.Zero,
                MaxConcurrentAgents: _registry.Count);
        }
    }

    /// <summary>
    /// Cleans up the registry by terminating all agents and clearing state.
    /// </summary>
    public void Cleanup()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;

            TerminateAllAgents("Registry disposed");
            _registry.Clear();
            _isDisposed = true;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic agent {AgentId} registered for crew {CrewId}. Role: {Role}, Reason: {Reason}")]
    private static partial void LogAgentRegistered(ILogger logger, AgentId agentId, CrewId crewId, object role, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic agent {AgentId} terminated in crew {CrewId}. Reason: {Reason}")]
    private static partial void LogAgentTerminated(ILogger logger, AgentId agentId, CrewId crewId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dynamic agent {AgentId} auto-terminated in crew {CrewId}. Reason: {Reason}")]
    private static partial void LogAgentAutoTerminated(ILogger logger, AgentId agentId, CrewId crewId, string reason);
}

/// <summary>
/// Metadata about a dynamically created agent.
/// </summary>
public sealed class DynamicAgentMetadata
{
    /// <summary>Gets the agent instance.</summary>
    public Domain.Agent.Agent Agent { get; }

    /// <summary>Gets the ID of the agent that requested this spawn.</summary>
    public AgentId? RequestingAgentId { get; }

    /// <summary>Gets the reason this agent was spawned.</summary>
    public string? SpawnReason { get; }

    /// <summary>Gets the spawn time.</summary>
    public DateTime SpawnTime { get; }

    /// <summary>Gets whether this agent has been terminated.</summary>
    public bool IsTerminated { get; private set; }

    /// <summary>Gets the termination time.</summary>
    public DateTime? TerminationTime { get; private set; }

    /// <summary>Gets the termination reason.</summary>
    public string? TerminationReason { get; private set; }

    /// <summary>Gets the agent's lifetime in milliseconds.</summary>
    public double LifetimeMs =>
        IsTerminated && TerminationTime.HasValue
            ? (TerminationTime.Value - SpawnTime).TotalMilliseconds
            : (DateTime.UtcNow - SpawnTime).TotalMilliseconds;

    /// <summary>Gets the agent's lifetime as a TimeSpan.</summary>
    public TimeSpan Lifetime => TimeSpan.FromMilliseconds(LifetimeMs);

    internal DynamicAgentMetadata(
        Domain.Agent.Agent agent,
        AgentId? requestingAgentId,
        string? spawnReason,
        DateTime spawnTime)
    {
        ArgumentNullException.ThrowIfNull(agent);
        Agent = agent;
        RequestingAgentId = requestingAgentId;
        SpawnReason = spawnReason;
        SpawnTime = spawnTime;
        IsTerminated = false;
    }

    internal void MarkTerminated(string? reason)
    {
        IsTerminated = true;
        TerminationTime = DateTime.UtcNow;
        TerminationReason = reason;
    }
}

/// <summary>
/// Statistics about a dynamic agent registry.
/// </summary>
public sealed record DynamicAgentRegistryStats(
    int TotalAgents,
    int ActiveAgents,
    int TerminatedAgents,
    TimeSpan AverageAgentLifetime,
    int MaxConcurrentAgents);
