using Orkeon.Application.Agent;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Agent;

/// <summary>
/// Default implementation of IDynamicAgentService for managing dynamic agent lifecycle.
/// Handles agent spawning, termination, and validation during crew execution.
/// </summary>
public sealed partial class DynamicAgentService : IDynamicAgentService
{
    private readonly IAgentFactory _agentFactory;
    private readonly ILogger<DynamicAgentService>? _logger;
    private readonly DynamicAgentServiceOptions _options;

    /// <summary>
    /// Creates a new instance of DynamicAgentService.
    /// </summary>
    /// <param name="agentFactory">The factory used to create agents.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="options">
    /// Optional configuration controlling whether crews may spawn dynamic agents and any
    /// concurrency caps. When omitted, the default policy allows dynamic agents without limit
    /// (preserving prior behaviour).
    /// </param>
    public DynamicAgentService(
        IAgentFactory agentFactory,
        ILogger<DynamicAgentService>? logger = null,
        DynamicAgentServiceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(agentFactory);
        _agentFactory = agentFactory;
        _logger = logger;
        _options = options ?? new DynamicAgentServiceOptions();
    }

    /// <summary>
    /// Spawns a new agent dynamically based on the provided request.
    /// </summary>
    public Task<Domain.Agent.Agent> SpawnAgentAsync(
        AgentSpawnRequest request,
        DynamicAgentRegistry registry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(registry);
        return SpawnAgentCoreAsync(request, registry, cancellationToken);
    }

    private async Task<Domain.Agent.Agent> SpawnAgentCoreAsync(
        AgentSpawnRequest request,
        DynamicAgentRegistry registry,
        CancellationToken cancellationToken)
    {
        // Check concurrent limit
        var activeCount = registry.DynamicAgentCount;
        if (activeCount > 0 && _logger is not null)
        {
            LogDynamicAgentCount(_logger, activeCount);
        }

        try
        {
            // Create the agent using the factory
            var agent = await _agentFactory.CreateAgentAsync(request, cancellationToken).ConfigureAwait(false);

            // Register it in the registry
            registry.RegisterAgent(
                agent,
                request.RequestingAgentId,
                $"Spawned by {(request.RequestingAgentId?.Value.ToString() ?? "system")}");

            if (_logger is not null)
            {
                var requestingAgent = request.RequestingAgentId?.Value.ToString() ?? "system";
                LogAgentSpawned(
                    _logger,
                    agent.Id,
                    request.Role,
                    request.ParentCrewId,
                    requestingAgent);
            }

            return agent;
        }
        catch (Exception ex)
        {
            if (_logger is not null)
                LogAgentSpawnFailed(_logger, ex, request.Role, request.ParentCrewId);

            throw;
        }
    }

    /// <summary>
    /// Terminates a dynamically created agent.
    /// </summary>
    public Task<bool> TerminateAgentAsync(
        AgentId agentId,
        DynamicAgentRegistry registry,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        ArgumentNullException.ThrowIfNull(registry);
        return TerminateAgentCoreAsync(agentId, registry, reason);
    }

    private async Task<bool> TerminateAgentCoreAsync(
        AgentId agentId,
        DynamicAgentRegistry registry,
        string? reason)
    {
        return await Task.Run(() =>
        {
            var result = registry.TerminateAgent(agentId, reason ?? "Terminated by service");

            if (result)
            {
                if (_logger is not null)
                {
                    LogAgentTerminated(_logger, agentId, reason ?? "Unknown");
                }
            }
            else
            {
                if (_logger is not null)
                    LogAgentTerminateMissing(_logger, agentId);
            }

            return result;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates whether a crew allows dynamic agents, based on the configured
    /// <see cref="DynamicAgentServiceOptions"/> (per-crew override &gt; global default).
    /// </summary>
    public Task<bool> AllowsDynamicAgentsAsync(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return AllowsDynamicAgentsCoreAsync(crewId);
    }

    private async Task<bool> AllowsDynamicAgentsCoreAsync(CrewId crewId)
    {
        var allowed = ResolveCrewOverride(crewId, out var crewPolicy)
            ? crewPolicy.AllowDynamicAgents
            : _options.AllowDynamicAgentsByDefault;

        if (_logger is not null)
        {
            LogCrewPolicy(_logger, crewId, allowed);
        }

        return await Task.FromResult(allowed).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the maximum concurrent dynamic agents allowed for a crew, based on the configured
    /// <see cref="DynamicAgentServiceOptions"/> (per-crew override &gt; global default).
    /// Returns <see langword="null"/> when unlimited.
    /// </summary>
    public Task<int?> GetMaxConcurrentDynamicAgentsAsync(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        return GetMaxConcurrentDynamicAgentsCoreAsync(crewId);
    }

    private async Task<int?> GetMaxConcurrentDynamicAgentsCoreAsync(CrewId crewId)
    {
        var max = ResolveCrewOverride(crewId, out var crewPolicy)
            ? crewPolicy.MaxConcurrentDynamicAgents
            : _options.DefaultMaxConcurrentDynamicAgents;

        if (_logger is not null)
        {
            var maxText = max?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unlimited";
            LogResolvedMaxConcurrent(_logger, crewId, maxText);
        }

        return await Task.FromResult(max).ConfigureAwait(false);
    }

    private bool ResolveCrewOverride(CrewId crewId, out DynamicAgentCrewPolicy crewPolicy)
    {
        return _options.CrewOverrides.TryGetValue(
            crewId.Value.ToString(),
            out crewPolicy!);
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Current dynamic agent count: {Count}")]
    static partial void LogDynamicAgentCount(ILogger logger, int count);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Dynamic agent spawned successfully. Agent ID: {AgentId}, Role: {Role}, Parent Crew: {ParentCrew}, Requesting Agent: {RequestingAgent}")]
    static partial void LogAgentSpawned(ILogger logger, AgentId agentId, AgentRole role, CrewId parentCrew, string requestingAgent);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error,
        Message = "Failed to spawn dynamic agent. Role: {Role}, Parent Crew: {ParentCrew}")]
    static partial void LogAgentSpawnFailed(ILogger logger, Exception ex, AgentRole role, CrewId parentCrew);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information,
        Message = "Dynamic agent terminated. Agent ID: {AgentId}, Reason: {Reason}")]
    static partial void LogAgentTerminated(ILogger logger, AgentId agentId, string reason);

    [LoggerMessage(EventId = 5, Level = LogLevel.Warning,
        Message = "Attempted to terminate non-existent dynamic agent. Agent ID: {AgentId}")]
    static partial void LogAgentTerminateMissing(ILogger logger, AgentId agentId);

    [LoggerMessage(EventId = 6, Level = LogLevel.Debug,
        Message = "Dynamic agent policy for crew {CrewId}: allowed={Allowed}")]
    static partial void LogCrewPolicy(ILogger logger, CrewId crewId, bool allowed);

    [LoggerMessage(EventId = 7, Level = LogLevel.Debug,
        Message = "Resolved max concurrent dynamic agents for crew {CrewId}: {Max}")]
    static partial void LogResolvedMaxConcurrent(ILogger logger, CrewId crewId, string max);
}
